using AssetKeeper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AssetKeeper.Tests.Persistence;

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _databaseName = $"asset_keeper_g0_test_{Guid.NewGuid():N}";
    private readonly string _adminConnection;

    private PostgresTestDatabase(string adminConnection)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnection);

        if (builder.Host is not "127.0.0.1" and not "localhost")
        {
            throw new InvalidOperationException("G0 database tests only run against local PostgreSQL.");
        }

        builder.Database = "postgres";
        _adminConnection = builder.ConnectionString;
        builder.Database = _databaseName;
        ConnectionString = builder.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync(bool migrate = true)
    {
        var database = new PostgresTestDatabase(Environment.GetEnvironmentVariable("ASSET_KEEPER_TEST_CONNECTION")!);
        await using var connection = new NpgsqlConnection(database._adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database._databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();

        try
        {
            if (migrate)
            {
                await using var context = database.CreateContext();
                await context.Database.MigrateAsync();
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
