namespace AssetKeeper.Application.History;

public sealed class HistoryValidationException : Exception
{
    public HistoryValidationException(string message) : base(message)
    {
    }
}
