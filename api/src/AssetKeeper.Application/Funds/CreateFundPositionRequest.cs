namespace AssetKeeper.Application.Funds;

public sealed record CreateFundPositionRequest(
    string Name,
    string? Code,
    string? ShareClass,
    string? Platform,
    decimal Shares,
    decimal Cost,
    decimal NetValue,
    DateOnly NetValueDate,
    string? Note,
    Guid RequestId,
    DateOnly? HoldingDate = null);
