using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Quotes;

public interface IStockQuoteProvider
{
    Task<ExternalStockQuote> GetLatestAsync(
        StockMarket market,
        string code,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalStockSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ExternalStockSearchResult>>([]);
    }
}
