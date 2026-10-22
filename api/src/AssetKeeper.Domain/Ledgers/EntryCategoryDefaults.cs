namespace AssetKeeper.Domain.Ledgers;

public static class EntryCategoryDefaults
{
    private static readonly string[] s_expenseNames =
    [
        "餐饮",
        "购物",
        "日用",
        "交通",
        "居家",
        "服饰",
        "娱乐",
        "通讯",
        "医疗",
        "学习",
        "运动",
        "旅行",
        "宠物",
        "人情",
        "其他"
    ];

    private static readonly (string Name, string? SystemKey)[] s_incomeNames =
    [
        ("工资", "salary"),
        ("奖金", null),
        ("其他收入", "other-income")
    ];

    public static IEnumerable<EntryCategory> CreateFor(Guid ledgerId, DateTimeOffset now)
    {
        var sortOrder = 0;

        foreach (var name in s_expenseNames)
        {
            yield return EntryCategory.Create(ledgerId, LedgerEntryKind.Expense, name, sortOrder, null, now);
            sortOrder++;
        }

        sortOrder = 0;

        foreach (var (name, systemKey) in s_incomeNames)
        {
            yield return EntryCategory.Create(ledgerId, LedgerEntryKind.Income, name, sortOrder, systemKey, now);
            sortOrder++;
        }
    }
}
