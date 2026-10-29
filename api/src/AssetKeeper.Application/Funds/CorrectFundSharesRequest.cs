namespace AssetKeeper.Application.Funds;

public sealed record CorrectFundSharesRequest(
    Guid PositionId,
    decimal Shares,
    string? Note,
    Guid RequestId,
    decimal? Cost = null);
