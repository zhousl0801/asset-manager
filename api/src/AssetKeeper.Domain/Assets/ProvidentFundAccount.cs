using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class ProvidentFundAccount
{
    public const decimal MaximumBalance = 1_000_000_000m;

    private ProvidentFundAccount()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public decimal Balance { get; private set; }

    public DateOnly AsOfDate { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public void RestoreProjection(decimal balance, DateOnly asOfDate, DateTimeOffset now)
    {
        Balance = balance;
        AsOfDate = asOfDate;
        UpdatedAt = now;
        Version++;
    }

    public static ProvidentFundAccount Create(Guid ledgerId, DateOnly asOfDate, DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (asOfDate == default)
        {
            throw new ArgumentException("余额日期无效。", nameof(asOfDate));
        }

        return new ProvidentFundAccount
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Balance = 0m,
            AsOfDate = asOfDate,
            UpdatedAt = now
        };
    }

    public void Deposit(decimal amount, DateOnly businessDate, DateTimeOffset now)
    {
        ValidateAmount(amount);
        Apply(amount, businessDate, now);
    }

    public void Withdraw(decimal amount, DateOnly businessDate, DateTimeOffset now)
    {
        ValidateAmount(amount);

        if (amount > Balance)
        {
            throw new InvalidOperationException("公积金余额不足。");
        }

        Apply(-amount, businessDate, now);
    }

    private void Apply(decimal effect, DateOnly businessDate, DateTimeOffset now)
    {
        if (businessDate == default)
        {
            throw new ArgumentException("业务日期无效。", nameof(businessDate));
        }

        var newBalance = Balance + effect;

        if (newBalance > MaximumBalance)
        {
            throw new InvalidOperationException("公积金余额超出允许范围。");
        }

        Balance = newBalance;

        if (businessDate > AsOfDate)
        {
            AsOfDate = businessDate;
        }

        UpdatedAt = now;
        Version++;
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("金额必须大于 0。", nameof(amount));
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("金额最多保留两位小数。", nameof(amount));
        }

        if (amount > MaximumBalance)
        {
            throw new ArgumentException("金额超出允许范围。", nameof(amount));
        }
    }
}
