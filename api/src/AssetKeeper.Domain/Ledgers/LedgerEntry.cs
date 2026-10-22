namespace AssetKeeper.Domain.Ledgers;

public sealed class LedgerEntry
{
    public const decimal MaximumAmount = 1_000_000_000m;
    public const int MaximumCategoryLength = 20;
    public const int MaximumNoteLength = 200;

    private LedgerEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public LedgerEntryKind Kind { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public string? Note { get; private set; }

    public LedgerEntrySource Source { get; private set; }

    public LedgerEntryStatus Status { get; private set; }

    public Guid RequestId { get; private set; }

    public long BusinessSequence { get; private set; }

    public Guid? RelatedCashAdjustmentId { get; private set; }

    public void SetHistoryMetadata(long sequence, Guid? relatedAdjustmentId)
    {
        BusinessSequence = sequence;
        RelatedCashAdjustmentId = relatedAdjustmentId;
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public string? ReversalReason { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public decimal CashEffect => Kind == LedgerEntryKind.Income ? Amount : -Amount;

    public static LedgerEntry Create(
        Guid ledgerId,
        LedgerEntryKind kind,
        string category,
        decimal amount,
        DateOnly businessDate,
        DateOnly ledgerStartDate,
        DateOnly businessToday,
        string? note,
        LedgerEntrySource source,
        Guid requestId,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("请求标识无效。", nameof(requestId));
        }

        var errors = Validate(kind, category, amount, businessDate, ledgerStartDate, businessToday, note);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Kind = kind,
            Category = category.Trim(),
            Amount = amount,
            BusinessDate = businessDate,
            Note = NormalizeNote(note),
            Source = source,
            Status = LedgerEntryStatus.Active,
            RequestId = requestId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 0
        };
    }

    public void Update(
        LedgerEntryKind kind,
        string category,
        decimal amount,
        DateOnly businessDate,
        DateOnly ledgerStartDate,
        DateOnly businessToday,
        string? note,
        DateTimeOffset now)
    {
        if (Status != LedgerEntryStatus.Active)
        {
            throw new InvalidOperationException("该记录已撤销，不能修改。");
        }

        var errors = Validate(kind, category, amount, businessDate, ledgerStartDate, businessToday, note);

        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        Kind = kind;
        Category = category.Trim();
        Amount = amount;
        BusinessDate = businessDate;
        Note = NormalizeNote(note);
        UpdatedAt = now;
        Version++;
    }

    public void Reverse(string? reason, DateTimeOffset now)
    {
        if (Status != LedgerEntryStatus.Active)
        {
            throw new InvalidOperationException("该记录已撤销。");
        }

        if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length > MaximumNoteLength)
        {
            throw new ArgumentException($"撤销原因不能超过 {MaximumNoteLength} 个字符。", nameof(reason));
        }

        Status = LedgerEntryStatus.Reversed;
        ReversalReason = string.IsNullOrWhiteSpace(reason) ? "用户撤销" : reason.Trim();
        ReversedAt = now;
        UpdatedAt = now;
        Version++;
    }

    public static List<string> Validate(
        LedgerEntryKind kind,
        string category,
        decimal amount,
        DateOnly businessDate,
        DateOnly ledgerStartDate,
        DateOnly businessToday,
        string? note)
    {
        var errors = new List<string>();

        if (kind != LedgerEntryKind.Income && kind != LedgerEntryKind.Expense)
        {
            errors.Add("收支方向无效。");
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            errors.Add("分类不能为空。");
        }
        else if (category.Trim().Length > MaximumCategoryLength)
        {
            errors.Add($"分类不能超过 {MaximumCategoryLength} 个字符。");
        }

        if (amount <= 0)
        {
            errors.Add("金额必须大于 0。");
        }
        else if (decimal.Round(amount, 2) != amount)
        {
            errors.Add("金额最多保留两位小数。");
        }
        else if (amount > MaximumAmount)
        {
            errors.Add("金额超出允许范围。");
        }

        if (businessDate == default)
        {
            errors.Add("业务日期无效。");
        }
        else if (businessDate < ledgerStartDate)
        {
            errors.Add("业务日期不能早于账本起始日期。");
        }
        else if (businessDate > businessToday)
        {
            errors.Add("业务日期不能晚于今天。");
        }

        if (!string.IsNullOrWhiteSpace(note) && note.Trim().Length > MaximumNoteLength)
        {
            errors.Add($"备注不能超过 {MaximumNoteLength} 个字符。");
        }

        return errors;
    }

    private static string? NormalizeNote(string? note)
    {
        return string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
