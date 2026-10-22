using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Api.Plans;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Plans;
using AssetKeeper.Application.Provident;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();

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
builder.Services.AddHttpClient<IGoldQuoteProvider, SinaGoldQuoteProvider>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<QuoteProviderOptions>>().Value;
    client.BaseAddress = new Uri(options.GoldBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetKeeper/1.0");
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<BusinessDateProvider>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
