using AssetKeeper.Application.Ledgers;

namespace AssetKeeper.Application.Funds;

public sealed record FundReturnDto(
    TrendRange Range,
    DateOnly From,
    DateOnly To,
    DateOnly IntervalFrom,
    decimal? OpeningAssetValue,
    decimal? ClosingAssetValue,
    decimal NetTransfer,
    decimal? ReconciliationDifference,
    decimal? IntervalProfit,
    decimal? IntervalRate,
    decimal? HoldingProfit,
    decimal? HoldingRate,
    int DisclosureDays,
    bool IsComplete,
    IReadOnlyList<FundReturnPointDto> Points,
    IReadOnlyList<FundReturnSummaryDto> Funds,
    IReadOnlyList<string> Notes);

public sealed record FundReturnPointDto(
    DateOnly Date,
    decimal? FundValue,
    decimal? DailyProfit,
    decimal? CumulativeProfit,
    int DisclosedFunds,
    int TotalFunds,
    int CoveredPositions,
    int TotalPositions,
    bool HasDisclosure,
    bool IsComplete,
    string Status);

public sealed record FundReturnSummaryDto(
    string Key,
    string Name,
    Guid? GroupId,
    string? GroupName,
    decimal? OpeningValue,
    decimal? ClosingValue,
    decimal? IntervalProfit,
    decimal? HoldingProfit,
    decimal? HoldingRate,
    decimal? CumulativeProfit,
    int DisclosureDays,
    DateOnly? LatestNetValueDate,
    bool IsComplete,
    IReadOnlyList<string> Notes);

public sealed record FundReturnDayDto(
    DateOnly Date,
    decimal? TotalProfit,
    int DisclosedFunds,
    int TotalFunds,
    int CoveredPositions,
    int TotalPositions,
    bool IsComplete,
    string Status,
    IReadOnlyList<FundReturnDayRowDto> Positions,
    IReadOnlyList<string> Notes);

public sealed record FundReturnDayRowDto(
    Guid PositionId,
    string Name,
    string? ShareClass,
    string? Platform,
    decimal? Value,
    decimal? DailyProfit,
    decimal? NetValue,
    DateOnly? NetValueDate,
    bool HasDisclosure,
    bool IsComplete,
    string Status,
    string? Note);
