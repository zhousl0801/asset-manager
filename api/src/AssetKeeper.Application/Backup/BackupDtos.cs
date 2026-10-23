namespace AssetKeeper.Application.Backup;

public sealed record BackupSummaryDto(
    Guid LedgerId,
    DateOnly StartDate,
    decimal CashBalance,
    decimal? ProvidentBalance,
    decimal FundValue,
    decimal FundPending,
    decimal FundPendingArrival,
    decimal? GoldValue,
    decimal GoldPendingArrival,
    decimal? TotalAssets,
    bool IsComplete,
    int EntryCount,
    int AssetTransactionCount,
    int PlanCount,
    int PlanOccurrenceCount,
    int MutationReceiptCount,
    int PriceObservationCount);

public sealed record BackupValidationDto(
    bool IsValid,
    bool WillReplace,
    string? ValidationToken,
    BackupSummaryDto Summary,
    BackupSummaryDto? Current,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> DuplicateRisks);

public sealed record RestoreBackupRequest(
    BackupDocument Backup,
    string ValidationToken,
    bool ReplaceExisting);

public sealed record RestoreBackupResultDto(
    bool Restored,
    bool ReplacedExisting,
    BackupSummaryDto Summary,
    DateTimeOffset RestoredAt,
    IReadOnlyList<string> Warnings);
