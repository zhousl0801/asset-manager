using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Cash;

public sealed class CashAdjustment
{
    public const int MaximumReasonLength = 100;

    private CashAdjustment()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public decimal PreviousBalance { get; private set; }

    public decimal NewBalance { get; private set; }

    public decimal Difference { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public long BusinessSequence { get; private set; }

    public decimal RecordedDifference { get; private set; }

    public bool IsReversed { get; private set; }

    public int Version { get; private set; }

    public void SetSequence(long sequence) => BusinessSequence = sequence;

    public void Recalculate(decimal previousBalance)
    {
        PreviousBalance = previousBalance;
        Difference = NewBalance - previousBalance;
    }

    public void Correct(decimal targetBalance, DateOnly date, string reason)
    {
        if (targetBalance < 0 || targetBalance > CashAccount.MaximumBalance || decimal.Round(targetBalance, 2) != targetBalance
            || date == default || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaximumReasonLength)
        {
            throw new ArgumentException("核对余额、日期或原因无效。");
        }

        NewBalance = targetBalance;
        BusinessDate = date;
        Reason = reason.Trim();
        Version++;
    }

    public void Reverse()
    {
        IsReversed = true;
        Version++;
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public static CashAdjustment Create(
        Guid ledgerId,
        decimal previousBalance,
        decimal newBalance,
        DateOnly businessDate,
        string reason,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (businessDate == default)
        {
            throw new ArgumentException("业务日期无效。", nameof(businessDate));
        }

        if (previousBalance < 0)
        {
            throw new ArgumentException("当前现金余额不能为负数。", nameof(previousBalance));
        }

        if (newBalance < 0)
        {
            throw new ArgumentException("核对后的现金余额不能为负数。", nameof(newBalance));
        }

        if (decimal.Round(previousBalance, 2) != previousBalance)
        {
            throw new ArgumentException("当前现金余额最多保留两位小数。", nameof(previousBalance));
        }

        if (decimal.Round(newBalance, 2) != newBalance)
        {
            throw new ArgumentException("核对后的现金余额最多保留两位小数。", nameof(newBalance));
        }

        if (newBalance > CashAccount.MaximumBalance)
        {
            throw new ArgumentException("核对后的现金余额超出允许范围。", nameof(newBalance));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("调整原因不能为空。", nameof(reason));
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > MaximumReasonLength)
        {
            throw new ArgumentException($"调整原因不能超过 {MaximumReasonLength} 个字符。", nameof(reason));
        }

        var difference = newBalance - previousBalance;

        if (difference == 0)
        {
            throw new ArgumentException("核对一致，无需调整。", nameof(newBalance));
        }

        return new CashAdjustment
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            PreviousBalance = previousBalance,
            NewBalance = newBalance,
            Difference = difference,
            RecordedDifference = difference,
            BusinessDate = businessDate,
            Reason = normalizedReason,
            CreatedAt = now
        };
    }
}
