namespace AssetKeeper.Application.Quotes;

public interface IFundQuoteProvider
{
    Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken);

    async Task<ExternalFundQuote> GetForDateAsync(string code, DateOnly date, CancellationToken cancellationToken)
    {
        var latest = await GetLatestAsync(code, cancellationToken);
        if (latest.EffectiveDate != date)
        {
            throw new QuoteUnavailableException($"基金行情服务未提供 {date:yyyy-MM-dd} 的历史净值。");
        }

        return latest;
    }

    Task<IReadOnlyList<ExternalFundSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ExternalFundSearchResult>>([]);
    }
}
