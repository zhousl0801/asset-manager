namespace AssetKeeper.Application.Quotes;

public sealed class QuoteRefreshOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 900;

    public bool RunOnStartup { get; set; }

    public int RetryCount { get; set; } = 1;

    public int RetryDelaySeconds { get; set; } = 3;

    public bool IncludeFunds { get; set; } = true;

    public bool IncludeGold { get; set; } = true;
}
