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
    int Version);

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
    int PlanVersion);

public sealed record PlanOverviewDto(
    IReadOnlyList<PlanDto> Plans,
    IReadOnlyList<PlanOccurrenceDto> PendingOccurrences,
    IReadOnlyList<PlanOccurrenceDto> RecentOccurrences,
    int ActiveCount,
    DateOnly? NextDueDate,
    decimal CashBalance,
    decimal ProvidentBalance);

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
    int? ExpectedVersion);

public sealed record PlanActionRequest(Guid RequestId);

public sealed record PlanPreviewRequest(DateOnly Date, decimal? TrialCash);

public sealed record PlanPreviewRow(
    Guid? PlanId,
    string PlanName,
    AutoPlanType Type,
    decimal Amount,
    string Status,
    decimal CashAfter,
    decimal ProvidentAfter,
    string? Note);

public sealed record PlanPreviewDto(
    DateOnly Date,
    decimal TrialCashStart,
    decimal TrialCashEnd,
    decimal ProvidentStart,
    decimal ProvidentEnd,
    IReadOnlyList<PlanPreviewRow> Rows,
    IReadOnlyList<string> Notes);

public sealed record PlanRunResultDto(
    int CreatedOccurrences,
    int Completed,
    int Waiting,
    int Failed);
