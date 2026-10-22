namespace AssetKeeper.Application.Quotes;

public interface IFundQuoteProvider
{
    Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken);
}
