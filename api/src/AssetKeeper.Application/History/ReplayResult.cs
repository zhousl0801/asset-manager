using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.History;

public sealed record HoldingReplay(Guid? PositionId, decimal Quantity, decimal Cost, decimal RealizedProfit,
    DateOnly? CoverageFrom, bool CanRebuild, decimal? Price, DateOnly? PriceDate, string? PriceSource,
    decimal PendingAmount = 0m)
{
    public decimal? Value => Price is null ? (Quantity == 0m && CoverageFrom is not null ? 0m : null) : decimal.Round(Quantity * Price.Value, 2);
}

public sealed record AdjustmentReplay(Guid Id, decimal PreviousBalance, decimal Difference);

public sealed record ReplayResult(
    DateOnly AsOfDate,
    decimal? CashBalance,
    DateOnly? CashCoverageFrom,
    DateOnly CashBalanceDate,
    decimal? ProvidentBalance,
    DateOnly? ProvidentCoverageFrom,
    DateOnly ProvidentBalanceDate,
    IReadOnlyList<HoldingReplay> Funds,
    HoldingReplay? Gold,
    IReadOnlyList<AdjustmentReplay> Adjustments,
    IReadOnlyList<string> Issues)
{
    public decimal KnownTotalAssets => (CashBalance ?? 0m) + (ProvidentBalance ?? 0m)
        + Funds.Sum(position => (position.Value ?? 0m) + position.PendingAmount) + (Gold?.Value ?? 0m);

    public decimal? TotalAssets => Issues.Count == 0 ? KnownTotalAssets : null;
}
