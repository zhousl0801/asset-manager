namespace AssetKeeper.Application.Cash;

public sealed record CashReconcileResult(
    CashReconcileStatus Status,
    CashReconcileResultDto? Result,
    IReadOnlyList<string> Errors)
{
    public static CashReconcileResult Success(CashReconcileResultDto result)
    {
        return new CashReconcileResult(CashReconcileStatus.Success, result, Array.Empty<string>());
    }

    public static CashReconcileResult NotFound(string message)
    {
        return new CashReconcileResult(CashReconcileStatus.NotFound, null, new[] { message });
    }

    public static CashReconcileResult Invalid(IReadOnlyList<string> errors)
    {
        return new CashReconcileResult(CashReconcileStatus.Invalid, null, errors);
    }

    public static CashReconcileResult Conflict(string message)
    {
        return new CashReconcileResult(CashReconcileStatus.Conflict, null, new[] { message });
    }
}
