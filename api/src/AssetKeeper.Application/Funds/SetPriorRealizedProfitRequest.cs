namespace AssetKeeper.Application.Funds;

/// <summary>录入建账前已实现收益（含以往卖出与分红），只影响累计收益。</summary>
public sealed record SetPriorRealizedProfitRequest(
    decimal Amount,
    string? Note,
    Guid RequestId);
