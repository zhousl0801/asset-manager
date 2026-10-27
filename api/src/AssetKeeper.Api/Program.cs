using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Api.Plans;
using AssetKeeper.Api.Quotes;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Provident;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Infrastructure.Backup;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Infrastructure.Quotes;
using AssetKeeper.Api.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

if (args is ["hash-password", var password])
{
    Console.WriteLine(AccessPasswordHasher.Hash(password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<AccessCredential>();
builder.Services.AddScoped<AccessCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "asset-keeper-access";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.EventsType = typeof(AccessCookieEvents);
    });
builder.Services.AddAuthorization(options =>
{
    // 生产环境始终要求登录；开发环境仅在配置了口令后才启用，便于本地验证完整流程。
    var requiresAccess = builder.Environment.IsProduction()
        || !string.IsNullOrWhiteSpace(builder.Configuration[AccessCredential.PasswordKey])
        || !string.IsNullOrWhiteSpace(builder.Configuration[AccessCredential.PasswordHashKey]);

    if (requiresAccess)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AccessRateLimiting.LoginPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
});

var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Host=localhost;Port=5432;Database=asset_keeper";
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ILedgerRepository, LedgerRepository>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<TrendService>();
builder.Services.AddScoped<MonthChangeService>();
builder.Services.AddScoped<LedgerEntryService>();
builder.Services.AddScoped<EntryStatsService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CashService>();
builder.Services.AddScoped<ProvidentFundService>();
builder.Services.AddScoped<GoldService>();
builder.Services.AddScoped<FundService>();
builder.Services.AddScoped<FundGroupService>();
builder.Services.AddScoped<FundReturnService>();
builder.Services.AddScoped<BackupService>();
builder.Services.Configure<QuoteRefreshOptions>(builder.Configuration.GetSection("QuoteRefreshScheduler"));
builder.Services.AddSingleton<QuoteRefreshMonitor>();
builder.Services.AddScoped<QuoteRefreshService>();
builder.Services.AddHostedService<QuoteRefreshSchedulerService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<IPlanRepository, PlanRepository>();
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<PlanExecutionService>();
builder.Services.Configure<PlanSchedulerOptions>(builder.Configuration.GetSection("PlanScheduler"));
builder.Services.AddHostedService<PlanSchedulerService>();
builder.Services.Configure<QuoteProviderOptions>(builder.Configuration.GetSection("QuoteProviders"));
builder.Services.AddHttpClient<IFundQuoteProvider, EastmoneyFundQuoteProvider>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<QuoteProviderOptions>>().Value;
    client.BaseAddress = new Uri(options.FundBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetKeeper/1.0");
});
builder.Services.AddHttpClient<IGoldQuoteProvider, IcbcGoldQuoteProvider>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<QuoteProviderOptions>>().Value;
    client.BaseAddress = new Uri(options.GoldBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetKeeper/1.0");
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<BusinessDateProvider>();

var app = builder.Build();

if (app.Environment.IsProduction() && !app.Services.GetRequiredService<AccessCredential>().IsConfigured)
{
    throw new InvalidOperationException("ASSET_KEEPER_ACCESS_PASSWORD_HASH or ASSET_KEEPER_ACCESS_PASSWORD must be configured in production.");
}

if (app.Environment.IsProduction())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // 生产使用 PostgreSQL；内存数据库（集成测试）没有迁移。
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program
{
}
