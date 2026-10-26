using System.Text.Json;

namespace AssetKeeper.Application.Plans;

/// <summary>
/// 三地按同一个本地日历日期判断；修订追加版本并设置未来生效日，保留旧版本。
/// </summary>
public sealed class TradingCalendar
{
    private static readonly (string Code, string Name)[] Markets = [("CN", "中国"), ("US", "美国"), ("HK", "香港")];
    private readonly IReadOnlyList<MarketCalendarYear> _versions;

    public static TradingCalendar Default { get; } = Load();

    public TradingCalendar(IReadOnlyList<MarketCalendarYear> versions)
    {
        if (versions.Any(version => !Markets.Any(market => market.Code == version.Market)
            || version.ClosedDates.Any(date => date.Year != version.Year)
            || version.HalfDays.Any(date => date.Year != version.Year)
            || version.ClosedDates.Intersect(version.HalfDays).Any())
            || versions.GroupBy(version => (version.Market, version.Year, version.EffectiveFrom)).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("市场日历数据无效或生效日期重复。", nameof(versions));
        }

        _versions = versions;
    }

    public TradingDayDto GetDay(DateOnly date)
    {
        var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var markets = Markets.Select(market =>
        {
            var calendar = _versions.Where(version => version.Market == market.Code
                && version.Year == date.Year && version.EffectiveFrom <= date)
                .OrderByDescending(version => version.EffectiveFrom).FirstOrDefault();
            var open = !weekend && !(calendar?.ClosedDates.Contains(date) ?? false);

            return new MarketDayDto(market.Code, market.Name, date.Year, open,
                open && (calendar?.HalfDays.Contains(date) ?? false), calendar is null,
                calendar?.Version, calendar?.SourceUrl, calendar?.PublishedOn, calendar?.VerifiedOn);
        }).ToList();

        return new TradingDayDto(date, markets.All(market => market.IsOpen), markets);
    }

    public DateOnly NextTradingDay(DateOnly after)
    {
        var date = after.AddDays(1);

        while (!GetDay(date).IsTradingDay)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    public IReadOnlyList<TradingDayDto> GetDays(DateOnly from, DateOnly to)
    {
        var days = new List<TradingDayDto>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(GetDay(date));
        }

        return days;
    }

    private static TradingCalendar Load()
    {
        var assembly = typeof(TradingCalendar).Assembly;
        var versions = new List<MarketCalendarYear>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.Contains(".Plans.Calendars.") && name.EndsWith(".json")))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            versions.AddRange(JsonSerializer.Deserialize<List<MarketCalendarYear>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }

        return new TradingCalendar(versions);
    }
}

public sealed record MarketCalendarYear(
    string Market,
    string Name,
    int Year,
    string Version,
    string SourceUrl,
    DateOnly? PublishedOn,
    DateOnly VerifiedOn,
    DateOnly EffectiveFrom,
    IReadOnlyList<DateOnly> ClosedDates,
    IReadOnlyList<DateOnly> HalfDays);

public sealed record MarketDayDto(
    string Market,
    string Name,
    int Year,
    bool IsOpen,
    bool IsHalfDay,
    bool UsesWeekdayFallback,
    string? Version,
    string? SourceUrl,
    DateOnly? PublishedOn,
    DateOnly? VerifiedOn);

public sealed record TradingDayDto(DateOnly Date, bool IsTradingDay, IReadOnlyList<MarketDayDto> Markets);
