using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Quotes;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Tests.Quotes;

internal sealed class TestFundQuotes(decimal netValue, DateOnly date, DateTimeOffset obtainedAt) : IFundQuoteProvider
{
    public static Task<AssetOperationResult<FundMutationResultDto>> CreatePositionAsync(
        ILedgerRepository repository, BusinessDateProvider dates, CreateFundPositionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Code is null)
        {
            // Legacy snapshots can lack a code; current creation requires a provider.
            return SeedLegacyAsync(repository, dates, request, cancellationToken);
        }
        var service = new FundService(repository, dates, new TestFundQuotes(request.NetValue, request.NetValueDate, dates.UtcNow));
        return service.CreatePositionAsync(request, cancellationToken);
    }

    private static Task<AssetOperationResult<FundMutationResultDto>> SeedLegacyAsync(
        ILedgerRepository repository, BusinessDateProvider dates, CreateFundPositionRequest request, CancellationToken token) =>
        new MutationExecutor(repository, dates).ExecuteAsync(
            "fund.opening", request.RequestId, request,
            async () =>
            {
                var ledger = (await repository.GetForUpdateAsync(token))!;
                var position = FundPosition.Create(ledger.Id, request.Name, request.Code, request.ShareClass,
                    request.Platform, request.Shares, request.Cost, request.NetValue, request.NetValueDate, dates.UtcNow,
                    request.ConfirmationDelayDays);
                var transaction = AssetTransaction.CreateFundOpening(ledger.Id, position.Id, request.Shares, request.Cost,
                    request.NetValue, position.DisplayName, request.NetValueDate, request.Note, request.RequestId, dates.UtcNow);
                await repository.AddFundPositionAsync(position, token);
                await new LedgerHistoryRebuilder(repository, dates).StageAsync(ledger, transaction, request.HoldingDate!.Value,
                    request.NetValueDate, null, token);
                await repository.SaveChangesAsync(token);
                var overview = (await new FundService(repository, dates).GetOverviewAsync(token))!;
                return AssetOperationResult<FundMutationResultDto>.Created(new(
                    overview.Positions.Single(p => p.Id == position.Id), overview.CashBalance,
                    overview.Activities.Single(a => a.Id == transaction.Id)));
            },
            result => result.Status == AssetOperationStatus.Created,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<FundMutationResultDto>.Conflict,
            message => AssetOperationResult<FundMutationResultDto>.Invalid(new[] { message }), token);

    public Task<ExternalFundQuote> GetLatestAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(new ExternalFundQuote(code, netValue, date, "test-provider", obtainedAt));

    public static async Task<AssetOperationResult<FundQuoteRefreshDto>> RefreshAsync(
        ILedgerRepository repository, BusinessDateProvider dates, Guid positionId, decimal netValue, DateOnly date)
    {
        var service = new FundService(repository, dates, new TestFundQuotes(netValue, date, dates.UtcNow));
        var result = await service.RefreshQuoteAsync(positionId, CancellationToken.None, confirmDueBuys: false);
        Assert.Equal(AssetOperationStatus.Success, result.Status);
        return result;
    }
}
