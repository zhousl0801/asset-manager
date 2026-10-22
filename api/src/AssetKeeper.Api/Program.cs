using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Cash;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Gold;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Provident;
using AssetKeeper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddScoped<LedgerEntryService>();
builder.Services.AddScoped<CashService>();
builder.Services.AddScoped<ProvidentFundService>();
builder.Services.AddScoped<GoldService>();
builder.Services.AddScoped<FundService>();
builder.Services.AddScoped<HistoryService>();
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
