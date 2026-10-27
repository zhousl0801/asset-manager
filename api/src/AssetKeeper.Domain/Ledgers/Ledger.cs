using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;

namespace AssetKeeper.Domain.Ledgers;

public sealed class Ledger
{
    public const string DefaultCurrency = "CNY";
    public const string DefaultTimeZone = "Asia/Shanghai";

    private Ledger()
    {
    }

    public Guid Id { get; private set; }

    public int SingletonSlot { get; private set; } = 1;

    public string Currency { get; private set; } = DefaultCurrency;

    public string TimeZone { get; private set; } = DefaultTimeZone;

    public DateOnly StartDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public long NextFactSequence { get; private set; }

    public bool FundNotHeld { get; private set; }

    public bool ProvidentFundNotHeld { get; private set; }

    public bool GoldNotHeld { get; private set; }

    public bool IsNotHeld(AssetType assetType)
    {
        return assetType switch
        {
            AssetType.Fund => FundNotHeld,
            AssetType.ProvidentFund => ProvidentFundNotHeld,
            AssetType.Gold => GoldNotHeld,
            _ => throw new ArgumentOutOfRangeException(nameof(assetType), assetType, "不支持的资产类型。")
        };
    }

    public bool SetNotHeld(AssetType assetType, bool notHeld, DateTimeOffset now)
    {
        var changed = notHeld != IsNotHeld(assetType);

        if (!changed)
        {
            return false;
        }

        switch (assetType)
        {
            case AssetType.Fund:
                FundNotHeld = notHeld;
                break;
            case AssetType.ProvidentFund:
                ProvidentFundNotHeld = notHeld;
                break;
            case AssetType.Gold:
                GoldNotHeld = notHeld;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(assetType), assetType, "不支持的资产类型。");
        }

        Touch(now);
        return true;
    }

    public long AllocateFactSequence() => ++NextFactSequence;

    public void ReserveFactSequence(long sequence)
    {
        if (sequence <= 0 || sequence == long.MaxValue)
        {
            throw new ArgumentException("业务顺序必须为有效正整数。", nameof(sequence));
        }

        NextFactSequence = Math.Max(NextFactSequence, sequence);
    }

    public void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    public CashAccount CashAccount { get; private set; } = null!;

    public void SetCashAccount(CashAccount cashAccount)
    {
        CashAccount = cashAccount;
    }

    public static Ledger Create(DateOnly startDate, DateOnly businessToday, DateTimeOffset now)
    {
        if (startDate == default)
        {
            throw new ArgumentException("起始日期无效。", nameof(startDate));
        }

        if (startDate > businessToday)
        {
            throw new ArgumentException("起始日期不能晚于今天。", nameof(startDate));
        }

        return new Ledger
        {
            Id = Guid.NewGuid(),
            Currency = DefaultCurrency,
            TimeZone = DefaultTimeZone,
            StartDate = startDate,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
