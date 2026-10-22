namespace AssetKeeper.Application.Quotes;

public interface IGoldQuoteProvider
{
    Task<ExternalGoldQuote> GetLatestAsync(CancellationToken cancellationToken);
}
