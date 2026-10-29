using AssetKeeper.Application.Plans;
using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Tests.Plans;

public sealed class TradingCalendarTests
{
    [Fact]
    public void MissingMarketYear_FallsBackIndependentlyAndPreservesKnownClosure()
    {
        var date = new DateOnly(2027, 1, 4);
        var calendar = new TradingCalendar([Version("US", date, new DateOnly(2027, 1, 1), [date])]);
        var day = calendar.GetDay(date);

        Assert.False(day.IsTradingDay);
        Assert.True(day.Markets.Single(market => market.Market == "CN").UsesWeekdayFallback);
        Assert.True(day.Markets.Single(market => market.Market == "HK").UsesWeekdayFallback);
        Assert.False(day.Markets.Single(market => market.Market == "US").UsesWeekdayFallback);
        Assert.False(calendar.GetDay(new DateOnly(2027, 1, 9)).IsTradingDay);
    }

    [Fact]
    public void AddedCalendarVersion_ChangesOnlyDatesFromEffectiveDate()
    {
        var past = new DateOnly(2027, 1, 4);
        var future = new DateOnly(2027, 1, 6);
        var oldVersion = Version("US", past, new DateOnly(2027, 1, 1), []);
        var newVersion = Version("US", future, new DateOnly(2027, 1, 5), [past, future]) with { Version = "v2" };
        var before = new TradingCalendar([oldVersion]);
        var after = new TradingCalendar([oldVersion, newVersion]);

        Assert.True(before.GetDay(past).IsTradingDay);
        Assert.True(after.GetDay(past).IsTradingDay);
        Assert.True(before.GetDay(future).IsTradingDay);
        Assert.False(after.GetDay(future).IsTradingDay);
        var firstImport = new TradingCalendar([newVersion]);
        Assert.True(firstImport.GetDay(past).IsTradingDay);
        Assert.True(firstImport.GetDay(past).Markets.Single(market => market.Market == "US").UsesWeekdayFallback);
    }

    [Fact]
    public void NextTradingDay_ChinaScope_IgnoresForeignClosures()
    {
        var after = new DateOnly(2026, 11, 25);

        Assert.Equal(new DateOnly(2026, 11, 26), TradingCalendar.Default.NextTradingDay(after, AutoPlanTradingScope.China));
        Assert.Equal(new DateOnly(2026, 11, 27), TradingCalendar.Default.NextTradingDay(after, AutoPlanTradingScope.ThreeMarkets));
    }

    [Fact]
    public void OfficialCoverage_ExposesSourcesAndHalfDays()
    {
        var day = TradingCalendar.Default.GetDay(new DateOnly(2026, 12, 24));
        Assert.True(day.IsTradingDay);
        Assert.All(day.Markets, market =>
        {
            Assert.False(market.UsesWeekdayFallback);
            Assert.StartsWith("https://", market.SourceUrl);
            Assert.NotNull(market.VerifiedOn);
        });
        Assert.Equal(2, day.Markets.Count(market => market.IsHalfDay));
        Assert.Equal(new DateOnly(2026, 10, 27), TradingCalendar.Default.NextTradingDay(new DateOnly(2026, 10, 26)));
    }

    [Fact]
    public void Month_ReturnsEveryDateAndMarksForeignMarketClosures()
    {
        var days = TradingCalendar.Default.GetMonth(2026, 10);

        Assert.Equal(31, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 31), days[^1].Date);

        var hongKongClosed = days.Single(day => day.Date == new DateOnly(2026, 10, 19));
        Assert.False(hongKongClosed.IsTradingDay);
        Assert.True(hongKongClosed.Markets.Single(market => market.Market == "CN").IsOpen);
        Assert.False(hongKongClosed.Markets.Single(market => market.Market == "HK").IsOpen);
        Assert.True(hongKongClosed.Markets.Single(market => market.Market == "US").IsOpen);
    }

    private static MarketCalendarYear Version(string market, DateOnly date, DateOnly effectiveFrom, IReadOnlyList<DateOnly> closed)
    {
        return new(market, market, date.Year, "v1", "https://example.test/calendar", effectiveFrom,
            effectiveFrom, effectiveFrom, closed, []);
    }
}
