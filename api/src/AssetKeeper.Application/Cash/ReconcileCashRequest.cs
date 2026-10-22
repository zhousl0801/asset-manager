namespace AssetKeeper.Application.Cash;

public sealed record ReconcileCashRequest(decimal ActualBalance, string? Reason, Guid RequestId = default, int ExpectedVersion = 0);
