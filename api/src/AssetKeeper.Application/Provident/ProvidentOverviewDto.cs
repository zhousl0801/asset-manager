namespace AssetKeeper.Application.Provident;

public sealed record ProvidentOverviewDto(
    bool IsConfigured,
    decimal Balance,
    DateOnly AsOfDate,
    decimal MonthDeposit,
    decimal MonthWithdrawal,
    decimal NetChange,
    IReadOnlyList<ProvidentActivityDto> Activities,
    DateOnly PeriodFrom,
    DateOnly PeriodTo);
