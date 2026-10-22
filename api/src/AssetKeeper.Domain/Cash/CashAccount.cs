using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Cash;

public sealed class CashAccount
{
    public const decimal MaximumBalance = 1_000_000_000m;

    private CashAccount()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public decimal Balance { get; private set; }

    public decimal? OpeningBalance { get; private set; }

    public DateOnly? OpeningDate { get; private set; }

    public bool HistoryConfirmed { get; private set; } = true;

    public void SetOpening(decimal balance, DateOnly date)
    {
        if (balance < 0 || balance > MaximumBalance || decimal.Round(balance, 2) != balance || date == default)
        {
            throw new ArgumentException("起始现金金额或日期无效。");
        }

        OpeningBalance = balance;
        OpeningDate = date;
        HistoryConfirmed = true;
    }

    public void RestoreProjection(decimal balance, DateOnly asOfDate, DateTimeOffset now)
    {
        Balance = balance;
        AsOfDate = asOfDate;
        UpdatedAt = now;
        Version++;
    }

    public DateOnly AsOfDate { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public void ApplyCashEffect(decimal effect, DateOnly businessDate, DateTimeOffset now)
    {
        if (businessDate == default)
        {
            throw new ArgumentException("业务日期无效。", nameof(businessDate));
        }

        if (decimal.Round(effect, 2) != effect)
        {
            throw new ArgumentException("现金变动金额最多保留两位小数。", nameof(effect));
        }

        var newBalance = Balance + effect;

        if (newBalance < 0)
        {
            throw new InvalidOperationException("现金余额不足。");
        }

        Balance = newBalance;

        if (businessDate > AsOfDate)
        {
            AsOfDate = businessDate;
        }

        UpdatedAt = now;
        Version++;
    }

    public static CashAccount Create(Guid ledgerId, decimal balance, DateOnly asOfDate, DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (balance < 0)
        {
            throw new ArgumentException("现金余额不能为负数。", nameof(balance));
        }

        if (decimal.Round(balance, 2) != balance)
        {
            throw new ArgumentException("现金余额最多保留两位小数。", nameof(balance));
        }

        if (balance > MaximumBalance)
        {
            throw new ArgumentException("现金余额超出允许范围。", nameof(balance));
        }

        if (asOfDate == default)
        {
            throw new ArgumentException("余额日期无效。", nameof(asOfDate));
        }

        return new CashAccount
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Balance = balance,
            OpeningBalance = balance,
            OpeningDate = asOfDate,
            AsOfDate = asOfDate,
            UpdatedAt = now
        };
    }
}
