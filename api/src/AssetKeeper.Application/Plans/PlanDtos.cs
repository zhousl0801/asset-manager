using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Application.Plans;

public sealed record PlanDto(
    Guid Id,
    AutoPlanType Type,
    string Name,
    decimal Amount,
    AutoPlanCycle Cycle,
    int? DayOfMonth,
    int? DayOfWeek,
    TimeOnly Time,
    DateOnly StartDate,
    DateOnly GenerateFrom,
    bool IsActive,
    Guid? FundPositionId,
    string? FundPositionName,
    DateOnly? NextDueDate,
    int Version,
    FundPlanTarget? FundTarget = null,
    IReadOnlyList<DateOnly>? UpcomingDates = null,
    decimal Fee = 0m,
    AutoPlanTradingScope TradingScope = AutoPlanTradingScope.ThreeMarkets);

public sealed record PlanOccurrenceDto(
    Guid Id,
    Guid PlanId,
    string PlanName,
    AutoPlanType Type,
    decimal Amount,
    DateOnly DueDate,
    TimeOnly DueTime,
    PlanOccurrenceStatus Status,
    Guid? RelatedRecordId,
    string? ErrorMessage,
    DateTimeOffset? ExecutedAt,
    string PeriodKey,
    int PlanVersion,
    decimal Fee = 0m);

public sealed record PlanOverviewDto(
    IReadOnlyList<PlanDto> Plans,
    IReadOnlyList<PlanOccurrenceDto> PendingOccurrences,
    int ActiveCount,
    DateOnly? NextDueDate,
    decimal CashBalance,
    decimal ProvidentBalance);

public sealed record PlanOccurrencePageDto(
    IReadOnlyList<PlanOccurrenceDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record PlanCalendarDto(
    int Year,
    int Month,
    IReadOnlyList<TradingDayDto> Days);

public sealed record SavePlanRequest(
    Guid? Id,
    AutoPlanType Type,
    string? Name,
    decimal Amount,
    AutoPlanCycle Cycle,
    int? DayOfMonth,
    int? DayOfWeek,
    TimeOnly Time,
    DateOnly StartDate,
    Guid? FundPositionId,
    Guid RequestId,
    int? ExpectedVersion,
    FundPlanTarget? FundTarget = null,
    decimal Fee = 0m,
    AutoPlanTradingScope TradingScope = AutoPlanTradingScope.ThreeMarkets);

public sealed record PlanActionRequest(Guid RequestId);

public sealed record RecordOccurrenceResultDto(
    PlanOccurrenceDto Occurrence,
    bool Recorded,
    string? Error);

public sealed record PlanRunResultDto(
    int CreatedOccurrences,
    int Completed,
    int Waiting,
    int Failed);

public sealed record FundPlanTarget(string Name, string? Code, string? ShareClass);
