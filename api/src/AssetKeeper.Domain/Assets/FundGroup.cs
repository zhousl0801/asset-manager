using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Domain.Assets;

public sealed class FundGroup
{
    public const int MaximumNameLength = 20;

    private FundGroup()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public Ledger Ledger { get; private set; } = null!;

    public static FundGroup Create(Guid ledgerId, string name, int sortOrder, DateTimeOffset now)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(ledgerId));
        }

        return new FundGroup
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            Name = NormalizeName(name),
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = NormalizeName(name);
        UpdatedAt = now;
        Version++;
    }

    public static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;

        if (normalized.Length == 0)
        {
            throw new ArgumentException("分组名称不能为空。", nameof(name));
        }

        if (normalized.Length > MaximumNameLength)
        {
            throw new ArgumentException($"分组名称不能超过 {MaximumNameLength} 个字符。", nameof(name));
        }

        return normalized;
    }
}
