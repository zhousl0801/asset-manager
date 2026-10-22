namespace AssetKeeper.Application.Provident;

public sealed record ProvidentDepositRequest(
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId);
