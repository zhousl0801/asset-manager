namespace AssetKeeper.Application.Ledgers;

public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("数据已被其他操作修改，请刷新后重试。", innerException)
    {
    }
}
