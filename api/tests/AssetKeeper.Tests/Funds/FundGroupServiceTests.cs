using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Infrastructure.Persistence;
using AssetKeeper.Tests.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Tests.Funds;

public sealed class FundGroupServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_AppendsGroupAndRejectsDuplicateName()
    {
        await using var context = await CreateAsync();

        var created = await context.Groups.CreateAsync(new("纳斯达克指数"), CancellationToken.None);
        var duplicate = await context.Groups.CreateAsync(new(" 纳斯达克指数 "), CancellationToken.None);

        Assert.Equal(FundGroupOperationStatus.Created, created.Status);
        Assert.Equal("纳斯达克指数", created.Group!.Name);
        Assert.Equal(FundGroupOperationStatus.Conflict, duplicate.Status);
        Assert.Single(await context.Groups.GetAsync(CancellationToken.None) ?? []);
    }

    [Fact]
    public async Task Assign_MovesAllShareClassesAndPlatformsOfTheSameFund()
    {
        await using var context = await CreateAsync();
        var group = (await context.Groups.CreateAsync(new("纳斯达克指数"), CancellationToken.None)).Group!;
        var position = context.Db.FundPositions.First();

        var result = await context.Groups.AssignAsync(
            position.Id,
            new(group.Id),
            CancellationToken.None);

        Assert.Equal(FundGroupOperationStatus.Success, result.Status);
        Assert.Equal(2, result.Assignment!.PositionIds.Count);
        Assert.All(context.Db.FundPositions, item => Assert.Equal(group.Id, item.FundGroupId));
    }

    [Fact]
    public async Task Assign_ToUnknownGroupReturnsNotFound()
    {
        await using var context = await CreateAsync();
        var position = context.Db.FundPositions.First();

        var result = await context.Groups.AssignAsync(
            position.Id,
            new(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(FundGroupOperationStatus.NotFound, result.Status);
        Assert.All(context.Db.FundPositions, item => Assert.Null(item.FundGroupId));
    }

    [Fact]
    public async Task RenameAndDelete_KeepPositionsAndReturnThemToUngrouped()
    {
        await using var context = await CreateAsync();
        var group = (await context.Groups.CreateAsync(new("纳斯达克指数"), CancellationToken.None)).Group!;
        var position = context.Db.FundPositions.First();
        await context.Groups.AssignAsync(position.Id, new(group.Id), CancellationToken.None);

        var renamed = await context.Groups.RenameAsync(group.Id, new("美股科技"), CancellationToken.None);
        var deleted = await context.Groups.DeleteAsync(group.Id, CancellationToken.None);
        var overview = await context.Funds.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(FundGroupOperationStatus.Success, renamed.Status);
        Assert.Equal("美股科技", renamed.Group!.Name);
        Assert.Equal(FundGroupOperationStatus.Success, deleted.Status);
        Assert.Empty(await context.Groups.GetAsync(CancellationToken.None) ?? []);
        Assert.All(overview!.Positions, item => Assert.Null(item.GroupId));
        Assert.Equal(2, overview.Positions.Count);
    }

    [Fact]
    public async Task Overview_AggregatesShareClassesWithoutMixingSharesAndIncludesGroup()
    {
        await using var context = await CreateAsync();
        var group = (await context.Groups.CreateAsync(new("纳斯达克指数"), CancellationToken.None)).Group!;
        var position = context.Db.FundPositions.First();
        await context.Groups.AssignAsync(position.Id, new(group.Id), CancellationToken.None);

        var overview = await context.Funds.GetOverviewAsync(CancellationToken.None);

        Assert.NotNull(overview);
        var fund = Assert.Single(overview.Funds ?? []);
        Assert.Equal("纳指基金", fund.Name);
        Assert.Equal(group.Id, fund.GroupId);
        Assert.Equal("纳斯达克指数", fund.GroupName);
        Assert.Equal(2, fund.Classes.Count);
        Assert.Collection(
            fund.Classes,
            aClass =>
            {
                Assert.Equal("A", aClass.ShareClass);
                Assert.Equal(100m, aClass.Shares);
                Assert.Equal("示例平台一", aClass.Platforms.Single().Platform);
            },
            cClass =>
            {
                Assert.Equal("C", cClass.ShareClass);
                Assert.Equal(50m, cClass.Shares);
                Assert.Equal("示例平台二", cClass.Platforms.Single().Platform);
            });
        Assert.Equal(2, fund.PlatformCount);
        Assert.Equal(1500m, fund.Value);
        Assert.Equal(1500m, fund.AssetValue);
        Assert.Single(overview.Groups ?? []);
        Assert.Equal(1, overview.Groups![0].FundCount);
        Assert.Equal(2, overview.Groups[0].PositionCount);
    }

    private static async Task<TestContext> CreateAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var repository = new LedgerRepository(db);
        var dates = new BusinessDateProvider(new FixedTimeProvider(Now));
        await new LedgerService(repository, dates).InitializeAsync(
            new InitializeLedgerRequest(Today.AddDays(-10), 10000m),
            CancellationToken.None);
        var funds = new FundService(repository, dates);
        await funds.CreatePositionAsync(
            new("纳指基金", "000001", "A", "示例平台一", 100m, 1000m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);
        await funds.CreatePositionAsync(
            new("纳指基金", "000002", "C", "示例平台二", 50m, 500m, 10m, Today, null, Guid.NewGuid(), HoldingDate: Today),
            CancellationToken.None);

        return new TestContext(
            db,
            repository,
            funds,
            new FundGroupService(repository, dates));
    }

    private sealed record TestContext(
        AppDbContext Db,
        LedgerRepository Repository,
        FundService Funds,
        FundGroupService Groups) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
