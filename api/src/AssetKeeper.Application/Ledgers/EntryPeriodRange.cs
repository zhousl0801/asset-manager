namespace AssetKeeper.Application.Ledgers;

public static class EntryPeriodRange
{
    public static (DateOnly From, DateOnly To) For(EntryPeriod period, DateOnly anchor)
    {
        return period switch
        {
            EntryPeriod.Day => (anchor, anchor),
            EntryPeriod.Month => (
                new DateOnly(anchor.Year, anchor.Month, 1),
                new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
            EntryPeriod.Year => (
                new DateOnly(anchor.Year, 1, 1),
                new DateOnly(anchor.Year, 12, 31)),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "统计周期无效。")
        };
    }
}
