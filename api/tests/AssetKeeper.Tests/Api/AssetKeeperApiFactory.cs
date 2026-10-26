using AssetKeeper.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AssetKeeper.Application.Quotes;

namespace AssetKeeper.Tests.Api;

public sealed class AssetKeeperApiFactory : WebApplicationFactory<Program>
{
    public TestQuoteProvider Quotes { get; } = new();
    public TimeProvider? Clock { get; init; }
    private readonly string _databaseName = $"asset-keeper-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlanScheduler:Enabled"] = "false",
                ["QuoteRefreshScheduler:Enabled"] = "false"
            });
        });
        builder.ConfigureServices(services =>
        {
            if (Clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Clock);
            }
            services.RemoveAll<IFundQuoteProvider>();
            services.AddSingleton<IFundQuoteProvider>(Quotes);
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    public sealed class TestQuoteProvider : IFundQuoteProvider
    {
        public decimal NetValue { get; set; } = 10m;
        public DateOnly Date { get; set; } = new(2026, 10, 20);

        public Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(new ExternalFundQuote(code, NetValue, Date, "test-provider", DateTimeOffset.UtcNow));
    }
}
