namespace AssetKeeper.Tests.Persistence;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASSET_KEEPER_TEST_CONNECTION")))
        {
            Skip = "Set ASSET_KEEPER_TEST_CONNECTION to a local PostgreSQL admin connection; each test creates and removes its own database.";
        }
    }
}
