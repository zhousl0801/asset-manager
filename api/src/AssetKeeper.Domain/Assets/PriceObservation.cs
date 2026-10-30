namespace AssetKeeper.Domain.Assets;

public sealed class PriceObservation
{
    private PriceObservation()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public AssetType AssetType { get; private set; }

    public Guid? FundPositionId { get; private set; }

    public Guid? StockPositionId { get; private set; }

    public Guid? PositionId => FundPositionId ?? StockPositionId;

    public Guid? SourceTransactionId { get; private set; }

    public decimal Price { get; private set; }

    public DateOnly EffectiveDate { get; private set; }

    public string Source { get; private set; } = "manual";

    public DateTimeOffset ObtainedAt { get; private set; }

    public long RevisionOrder { get; private set; }

    public bool IsSuperseded { get; private set; }

    public void Supersede() => IsSuperseded = true;

    public static PriceObservation Create(Guid ledgerId, AssetType assetType, Guid? positionId, Guid? transactionId,
        decimal price, DateOnly effectiveDate, DateTimeOffset now, long revisionOrder, string? source = null)
    {
        if (price <= 0 || price > AssetTransaction.MaximumPrice || decimal.Round(price, 4) != price || effectiveDate == default)
        {
            throw new ArgumentException("行情价格或有效日期无效。");
        }

        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "manual" : source.Trim();

        if (normalizedSource.Length > AssetTransaction.MaximumQuoteSourceLength)
        {
            throw new ArgumentException("行情来源过长。", nameof(source));
        }

        return new PriceObservation
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            AssetType = assetType,
            FundPositionId = assetType == AssetType.Stock ? null : positionId,
            StockPositionId = assetType == AssetType.Stock ? positionId : null,
            SourceTransactionId = transactionId,
            Price = price,
            EffectiveDate = effectiveDate,
            Source = normalizedSource,
            ObtainedAt = now,
            RevisionOrder = revisionOrder
        };
    }
}
