namespace AssetKeeper.Application.Quotes;

public enum QuoteRefreshScope
{
    All,
    Funds,
    Gold,
    Stocks
}

public sealed record QuoteRefreshRequest(QuoteRefreshScope Scope = QuoteRefreshScope.All);

public sealed record QuoteRefreshItemDto(
    string AssetType,
    Guid? PositionId,
    string Name,
    string? Code,
    string? Platform,
    bool Success,
    bool Updated,
    int Attempts,
    string? Source,
    decimal? Price,
    DateOnly? EffectiveDate,
    DateTimeOffset? LastAttemptAt,
    string? Error);

public sealed record QuoteRefreshRunDto(
    string Trigger,
    QuoteRefreshScope Scope,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    long DurationMs,
    int FundAttempted,
    int FundSucceeded,
    int FundFailed,
    int GoldAttempted,
    int GoldSucceeded,
    int GoldFailed,
    int StockAttempted,
    int StockSucceeded,
    int StockFailed,
    int Updated,
    IReadOnlyList<QuoteRefreshItemDto> Items,
    IReadOnlyList<string> Errors);

public sealed record QuoteStatusItemDto(
    string AssetType,
    Guid? PositionId,
    string Name,
    string? Code,
    string? Platform,
    decimal? Price,
    DateOnly? PriceDate,
    DateTimeOffset? LastAttemptAt,
    string? LastSource,
    string? LastError,
    string Status);

public sealed record QuoteStatusDto(
    bool SchedulerEnabled,
    int IntervalSeconds,
    DateTimeOffset? NextRunAt,
    bool IsRunning,
    QuoteRefreshRunDto? LastRun,
    IReadOnlyList<QuoteStatusItemDto> Items,
    DateTimeOffset CheckedAt);
