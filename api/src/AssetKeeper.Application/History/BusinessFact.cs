using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.History;

public sealed record BusinessFact
{
    public Guid Id { get; init; }

    public string Source { get; init; } = string.Empty;

    public AssetType? AssetType { get; init; }

    public Guid? PositionId { get; init; }

    public AssetTransactionKind? Kind { get; init; }

    public LedgerEntryKind? EntryKind { get; init; }

    public string? Category { get; init; }

    public DateOnly Date { get; init; }

    public long Sequence { get; init; }

    public decimal Amount { get; init; }

    public decimal CashEffect { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Price { get; init; }

    public DateOnly? PriceDate { get; init; }

    public decimal Fee { get; init; }

    public Guid? LinkedTransactionId { get; init; }

    public bool IsReversed { get; init; }

    public bool NeedsDateConfirmation { get; init; }

    public Guid? RelatedCashAdjustmentId { get; init; }

    public decimal? TargetBalance { get; init; }

    public decimal? RecordedDifference { get; init; }

    public string? Subject { get; init; }

    public string? Note { get; init; }

    public int Version { get; init; }

    public static IReadOnlyList<BusinessFact> From(LedgerReplayData data)
    {
        return data.Entries.Select(entry => new BusinessFact
        {
            Id = entry.Id,
            Source = "entry",
            EntryKind = entry.Kind,
            Category = entry.Category,
            Date = entry.BusinessDate,
            Sequence = entry.BusinessSequence,
            Amount = entry.Amount,
            CashEffect = entry.CashEffect,
            IsReversed = entry.Status == LedgerEntryStatus.Reversed,
            RelatedCashAdjustmentId = entry.RelatedCashAdjustmentId,
            Note = entry.Note,
            Version = entry.Version
        }).Concat(data.Adjustments.Select(adjustment => new BusinessFact
        {
            Id = adjustment.Id,
            Source = "adjustment",
            Date = adjustment.BusinessDate,
            Sequence = adjustment.BusinessSequence,
            Amount = adjustment.Difference,
            TargetBalance = adjustment.NewBalance,
            RecordedDifference = adjustment.RecordedDifference,
            Note = adjustment.Reason,
            IsReversed = adjustment.IsReversed,
            Version = adjustment.Version
        })).Concat(data.Transactions.Select(transaction => new BusinessFact
        {
            Id = transaction.Id,
            Source = "asset",
            AssetType = transaction.AssetType,
            PositionId = transaction.PositionId,
            Kind = transaction.Kind,
            Date = transaction.BusinessDate,
            Sequence = transaction.BusinessSequence,
            Amount = transaction.Amount,
            CashEffect = transaction.CashEffect,
            Quantity = transaction.Quantity,
            Subject = transaction.Subject,
            Price = transaction.Price,
            PriceDate = transaction.PriceDate,
            Fee = transaction.Fee,
            LinkedTransactionId = transaction.LinkedTransactionId,
            Note = transaction.Note,
            IsReversed = transaction.IsReversed,
            NeedsDateConfirmation = transaction.NeedsDateConfirmation,
            RelatedCashAdjustmentId = transaction.RelatedCashAdjustmentId,
            Version = transaction.Version
        })).ToList();
    }
}
