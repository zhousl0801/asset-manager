namespace AssetKeeper.Application.Ledgers;

public sealed record UpdateAssetCoverageRequest(
    bool FundNotHeld,
    bool ProvidentFundNotHeld,
    bool GoldNotHeld,
    bool CoverageConfirmed);

public enum AssetCoverageStatus
{
    Updated,
    NotFound,
    Invalid,
    Conflict
}

public sealed record AssetCoverageResult(
    AssetCoverageStatus Status,
    LedgerOverviewDto? Overview,
    IReadOnlyList<string> Errors)
{
    public static AssetCoverageResult Updated(LedgerOverviewDto overview) => new(AssetCoverageStatus.Updated, overview, []);

    public static AssetCoverageResult NotFound() => new(AssetCoverageStatus.NotFound, null, []);

    public static AssetCoverageResult Invalid(params string[] errors) => new(AssetCoverageStatus.Invalid, null, errors);

    public static AssetCoverageResult Conflict() => new(AssetCoverageStatus.Conflict, null, ["账本已发生变化，请刷新后重试。"]);
}
