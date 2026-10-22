using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Ledgers;

public sealed class BusinessDateProvider
{
    private static readonly TimeZoneInfo BusinessTimeZone = TimeZoneInfo.FindSystemTimeZoneById(Ledger.DefaultTimeZone);

    private readonly TimeProvider _timeProvider;

    public BusinessDateProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, BusinessTimeZone).DateTime);

    public DateTimeOffset ToBusinessInstant(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, BusinessTimeZone);

        return new DateTimeOffset(utc);
    }
}
