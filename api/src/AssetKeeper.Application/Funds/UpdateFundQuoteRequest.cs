namespace AssetKeeper.Application.Funds;

public sealed record UpdateFundQuoteRequest(
    Guid PositionId,
    decimal NetValue,
    DateOnly NetValueDate,
    Guid RequestId);
