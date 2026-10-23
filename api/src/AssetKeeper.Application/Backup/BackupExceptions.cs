namespace AssetKeeper.Application.Backup;

public sealed class BackupValidationException : Exception
{
    public BackupValidationException(IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class BackupConflictException : Exception
{
    public BackupConflictException(string message)
        : base(message)
    {
    }
}
