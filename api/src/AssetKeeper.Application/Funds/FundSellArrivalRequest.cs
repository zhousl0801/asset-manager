namespace AssetKeeper.Application.Funds;

public sealed record FundSellArrivalRequest(
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
