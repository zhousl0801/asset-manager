namespace AssetKeeper.Application.Ledgers;

public sealed record EntryOperationResult(
    EntryOperationStatus Status,
    EntryMutationResultDto? Result,
    IReadOnlyList<string> Errors)
{
    public static EntryOperationResult Success(EntryMutationResultDto result)
    {
        return new EntryOperationResult(EntryOperationStatus.Success, result, Array.Empty<string>());
    }

    public static EntryOperationResult Created(EntryMutationResultDto result)
    {
        return new EntryOperationResult(EntryOperationStatus.Created, result, Array.Empty<string>());
    }

    public static EntryOperationResult NotFound(string message)
    {
        return new EntryOperationResult(EntryOperationStatus.NotFound, null, new[] { message });
    }

    public static EntryOperationResult Invalid(IReadOnlyList<string> errors)
    {
        return new EntryOperationResult(EntryOperationStatus.Invalid, null, errors);
    }

    public static EntryOperationResult Conflict(string message)
    {
        return new EntryOperationResult(EntryOperationStatus.Conflict, null, new[] { message });
    }
}
