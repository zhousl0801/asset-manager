namespace AssetKeeper.Application.Funds;

public sealed record FundSummaryDto(
    string Key,
    string Name,
    Guid? GroupId,
    string? GroupName,
    decimal Value,
    decimal Cost,
    decimal HoldingProfit,
    decimal RealizedProfit,
    decimal PendingBuyAmount,
    decimal PendingArrivalAmount,
    decimal AssetValue,
    int PlatformCount,
    DateOnly? LatestNetValueDate,
    bool HistoryConfirmed,
    bool HasEffectiveQuotes,
    IReadOnlyList<FundShareClassDto> Classes);

public sealed record FundShareClassDto(
    string? ShareClass,
    decimal Shares,
    decimal Cost,
    decimal Value,
    decimal HoldingProfit,
    decimal RealizedProfit,
    decimal? NetValue,
    DateOnly? NetValueDate,
    IReadOnlyList<FundPlatformHoldingDto> Platforms,
    string Key,
    string? Code,
    string IdentityBasis);

public sealed record FundPlatformHoldingDto(
    Guid PositionId,
    string? Platform,
    string Name,
    string? Code,
    string? ShareClass,
    decimal Shares,
    decimal Cost,
    decimal NetValue,
    DateOnly NetValueDate,
    decimal Value,
    decimal HoldingProfit,
    decimal RealizedProfit,
    bool HasEffectiveQuote,
    int ConfirmationDelayDays = 2);
