namespace AssetKeeper.Application.Funds;

public sealed record ConfirmFundBuyRequest(
    decimal Shares,
    decimal NetValue,
    DateOnly NetValueDate,
    decimal Fee,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
