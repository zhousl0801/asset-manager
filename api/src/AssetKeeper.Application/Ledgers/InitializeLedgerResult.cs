namespace AssetKeeper.Application.Ledgers;

public sealed record InitializeLedgerResult(
    InitializeLedgerStatus Status,
    LedgerOverviewDto? Overview,
    IReadOnlyList<string> Errors)
{
    public static InitializeLedgerResult Created(LedgerOverviewDto overview)
    {
        return new InitializeLedgerResult(InitializeLedgerStatus.Created, overview, Array.Empty<string>());
    }

    public static InitializeLedgerResult AlreadyExists()
    {
        return new InitializeLedgerResult(InitializeLedgerStatus.AlreadyExists, null, Array.Empty<string>());
    }

    public static InitializeLedgerResult Invalid(IReadOnlyList<string> errors)
    {
        return new InitializeLedgerResult(InitializeLedgerStatus.Invalid, null, errors);
    }
}
