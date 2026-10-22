namespace AssetKeeper.Application.Provident;

public sealed record ProvidentWithdrawalRequest(
    decimal Amount,
    DateOnly BusinessDate,
    string? Note,
    Guid RequestId,
    Guid? RelatedCashAdjustmentId = null);
