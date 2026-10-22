namespace AssetKeeper.Application.Provident;

public sealed record ProvidentOpeningRequest(decimal Balance, DateOnly BusinessDate, string? Note, Guid RequestId);
