namespace AssetKeeper.Application.Gold;

public sealed record GoldSellArrivalRequest(
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
