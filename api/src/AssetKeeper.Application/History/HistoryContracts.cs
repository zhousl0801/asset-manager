using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.History;

public sealed record HistoryChangeRequest(
    Guid RequestId,
    int ExpectedLedgerVersion,
    string Source,
    Guid FactId,
    int ExpectedFactVersion,
    string Action,
    DateOnly Date,
    long Sequence,
    decimal Amount,
    decimal? Quantity,
    decimal? Price,
    DateOnly? PriceDate,
    LedgerEntryKind? EntryKind,
    string? Category,
    string? Note,
    string Reason,
    Guid? RelatedCashAdjustmentId = null,
    decimal? Fee = null);

public sealed record ConfirmHistoryChangeRequest(HistoryChangeRequest Change, string PreviewToken);

public sealed record HistoryPreviewDto(int LedgerVersion, ReplayResult Before, ReplayResult After, string PreviewToken);

public sealed record HistoryAuditDto(Guid RequestId, DateTimeOffset CreatedAt, string? BeforeJson);

public sealed record HistoryOverviewDto(int LedgerVersion, BusinessFact CashOpening, IReadOnlyList<BusinessFact> Facts,
    IReadOnlyList<HistoryPriceDto> Prices, IReadOnlyList<HistoryAuditDto> Audit, ReplayResult Snapshot);

public sealed record HistoryPriceDto(Guid Id, Guid? PositionId, string AssetType, decimal Price, DateOnly EffectiveDate,
    string Source, DateTimeOffset ObtainedAt, bool IsSuperseded);
