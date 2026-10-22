namespace AssetKeeper.Application.Assets;

public sealed record AssetOperationResult<T>(
    AssetOperationStatus Status,
    T? Result,
    IReadOnlyList<string> Errors)
    where T : class
{
    public static AssetOperationResult<T> Success(T result)
    {
        return new AssetOperationResult<T>(AssetOperationStatus.Success, result, Array.Empty<string>());
    }

    public static AssetOperationResult<T> Created(T result)
    {
        return new AssetOperationResult<T>(AssetOperationStatus.Created, result, Array.Empty<string>());
    }

    public static AssetOperationResult<T> NotFound(string message)
    {
        return new AssetOperationResult<T>(AssetOperationStatus.NotFound, null, new[] { message });
    }

    public static AssetOperationResult<T> Invalid(IReadOnlyList<string> errors)
    {
        return new AssetOperationResult<T>(AssetOperationStatus.Invalid, null, errors);
    }

    public static AssetOperationResult<T> Conflict(string message)
    {
        return new AssetOperationResult<T>(AssetOperationStatus.Conflict, null, new[] { message });
    }
}
