namespace AssetKeeper.Domain.Ledgers;

public sealed class EntryCategory
{
    public const int MaximumNameLength = 20;
    public const int MaximumSystemKeyLength = 32;

    private EntryCategory()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public LedgerEntryKind Kind { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public string? SystemKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public void Rename(string name, DateTimeOffset now)
    {
        var normalized = NormalizeName(name);
        Name = normalized;
        UpdatedAt = now;
        Version++;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
        Version++;
    }

    public void SetSortOrder(int sortOrder, DateTimeOffset now)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentException("分类顺序不能为负数。", nameof(sortOrder));
        }

        SortOrder = sortOrder;
        UpdatedAt = now;
    }

    public static EntryCategory Create(
        Guid ledgerId,
        LedgerEntryKind kind,
        string name,
        int sortOrder,
        string? systemKey,
        DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentException("分类顺序不能为负数。", nameof(sortOrder));
        }

        var normalizedKey = NormalizeSystemKey(systemKey);

        return new EntryCategory
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Kind = kind,
            Name = NormalizeName(name),
            SortOrder = sortOrder,
            IsActive = true,
            SystemKey = normalizedKey,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static string NormalizeName(string name)
    {
        var normalized = name.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("分类名称不能为空。", nameof(name));
        }

        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException($"分类名称不能超过 {MaximumNameLength} 个字符。", nameof(name));
        }

        return normalized;
    }

    private static string? NormalizeSystemKey(string? systemKey)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
        {
            return null;
        }

        var normalized = systemKey.Trim();

        if (normalized.Length > MaximumSystemKeyLength)
        {
            throw new ArgumentException($"系统标识不能超过 {MaximumSystemKeyLength} 个字符。", nameof(systemKey));
        }

        return normalized;
    }
}
