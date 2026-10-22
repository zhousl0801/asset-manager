namespace AssetKeeper.Application.Funds;

public sealed record ConfirmFundSellRequest(
    decimal Shares,
    decimal NetValue,
    DateOnly NetValueDate,
    decimal Fee,
    decimal NetAmount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
