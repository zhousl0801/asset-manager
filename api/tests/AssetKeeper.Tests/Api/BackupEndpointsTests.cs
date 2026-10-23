using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetKeeper.Application.Backup;
using AssetKeeper.Application.Ledgers;

namespace AssetKeeper.Tests.Api;

public sealed class BackupEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task BackupValidateAndRestore_ReplacesWithoutDuplicatingEntries()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(client);
        await CreateEntryAsync(client);

        var backupResponse = await client.GetAsync("/api/ledger/backup");

        Assert.Equal(HttpStatusCode.OK, backupResponse.StatusCode);
        var backup = await backupResponse.Content.ReadFromJsonAsync<BackupDocument>(JsonOptions);
        Assert.NotNull(backup);
        Assert.Equal(BackupDocument.CurrentFormat, backup.Format);
        Assert.Single(backup.Entries);

        var validateResponse = await client.PostAsJsonAsync("/api/ledger/backup/validate", backup, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var validation = await validateResponse.Content.ReadFromJsonAsync<BackupValidationDto>(JsonOptions);
        Assert.NotNull(validation);
        Assert.True(validation.IsValid);
        Assert.NotNull(validation.ValidationToken);
        Assert.True(validation.WillReplace);

        var badRestore = await client.PostAsJsonAsync("/api/ledger/backup/restore", new
        {
            backup,
            validationToken = "wrong",
            replaceExisting = true
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, badRestore.StatusCode);

        var restoreResponse = await client.PostAsJsonAsync("/api/ledger/backup/restore", new
        {
            backup,
            validationToken = validation.ValidationToken,
            replaceExisting = true
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restored = await restoreResponse.Content.ReadFromJsonAsync<RestoreBackupResultDto>(JsonOptions);
        Assert.NotNull(restored);
        Assert.True(restored.Restored);
        Assert.True(restored.ReplacedExisting);

        var period = await client.GetFromJsonAsync<EntryPeriodDto>(
            "/api/ledger/entries?period=month&anchor=2026-10-02",
            JsonOptions);
        Assert.NotNull(period);
        Assert.Single(period.Entries);
    }

    [Fact]
    public async Task DetailExport_ReturnsCsv()
    {
        using var factory = new AssetKeeperApiFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(client);
        await CreateEntryAsync(client);

        var response = await client.GetAsync("/api/ledger/export/details.csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("日期,业务类型,方向,金额", content);
        Assert.Contains("工资", content);
    }

    private static Task InitializeAsync(HttpClient client)
    {
        return client.PostAsJsonAsync("/api/ledger/initialize", new
        {
            startDate = "2026-10-01",
            cashBalance = 10000m
        });
    }

    private static async Task CreateEntryAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/ledger/entries", new
        {
            kind = "income",
            category = "工资",
            amount = 1000m,
            businessDate = "2026-10-02",
            note = "工资到账",
            requestId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
