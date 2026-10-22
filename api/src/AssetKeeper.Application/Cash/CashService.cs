using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;
using AssetKeeper.Domain.Cash;
using AssetKeeper.Domain.Ledgers;

namespace AssetKeeper.Application.Cash;

public sealed class CashService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public CashService(ILedgerRepository ledgerRepository, BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<CashOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        var (from, to) = EntryPeriodRange.For(EntryPeriod.Month, _businessDateProvider.Today);
        var entries = await _ledgerRepository.GetEntriesAsync(ledger.Id, from, to, cancellationToken);
        var adjustments = await _ledgerRepository.GetCashAdjustmentsAsync(ledger.Id, from, to, cancellationToken);
        var assetTransactions = await _ledgerRepository.GetAssetCashTransactionsAsync(
            ledger.Id,
            from,
            to,
            cancellationToken);

        var entryEffect = entries.Sum(entry => entry.CashEffect);
        var adjustmentTotal = adjustments.Sum(adjustment => adjustment.Difference);
        var assetCashEffect = assetTransactions.Sum(transaction => transaction.CashEffect);
        var openingBalance = ledger.CashAccount.Balance - entryEffect - adjustmentTotal - assetCashEffect;
        var inflow = entries
            .Where(entry => entry.CashEffect > 0)
            .Sum(entry => entry.CashEffect)
            + assetTransactions
                .Where(transaction => transaction.CashEffect > 0)
                .Sum(transaction => transaction.CashEffect);
        var outflow = entries
            .Where(entry => entry.CashEffect < 0)
            .Sum(entry => -entry.CashEffect)
            + assetTransactions
                .Where(transaction => transaction.CashEffect < 0)
                .Sum(transaction => -transaction.CashEffect);
        var activities = BuildActivities(entries, adjustments, assetTransactions);

        return new CashOverviewDto(
            ledger.CashAccount.Balance,
            ledger.CashAccount.AsOfDate,
            from,
            to,
            openingBalance,
            inflow,
            outflow,
            adjustmentTotal,
            ledger.CashAccount.Balance - openingBalance,
            activities,
            ledger.CashAccount.Version);
    }

    public Task<CashReconcileResult> ReconcileAsync(ReconcileCashRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "cash.reconcile", request.RequestId, request,
            () => ReconcileCoreAsync(request, cancellationToken),
            result => result.Status is CashReconcileStatus.Success,
            result => result with { Status = CashReconcileStatus.Success },
            CashReconcileResult.Conflict,
            message => CashReconcileResult.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<CashReconcileResult> ReconcileCoreAsync(
        ReconcileCashRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return CashReconcileResult.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.ExpectedVersion != ledger.CashAccount.Version)
        {
            return CashReconcileResult.Conflict("现金余额已变化，请刷新后重新确认核对差额。");
        }

        var errors = ValidateBalance(request.ActualBalance);

        if (errors.Count > 0)
        {
            return CashReconcileResult.Invalid(errors);
        }

        var difference = request.ActualBalance - ledger.CashAccount.Balance;

        if (difference == 0)
        {
            ledger.CashAccount.ApplyCashEffect(0m, _businessDateProvider.Today, _businessDateProvider.UtcNow);
            return CashReconcileResult.Success(
                new CashReconcileResultDto(true, 0m, ledger.CashAccount.Balance, null));
        }

        errors.AddRange(ValidateReason(request.Reason));

        if (errors.Count > 0)
        {
            return CashReconcileResult.Invalid(errors);
        }

        var now = _businessDateProvider.UtcNow;
        var businessToday = _businessDateProvider.Today;
        var adjustment = CashAdjustment.Create(
            ledger.Id,
            ledger.CashAccount.Balance,
            request.ActualBalance,
            businessToday,
            request.Reason!,
            now);

        adjustment.SetSequence(ledger.AllocateFactSequence());
        await _ledgerRepository.AddCashAdjustmentAsync(adjustment, cancellationToken);
        await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).RebuildAsync(ledger.Id, adjustment.Id, cancellationToken);


        return CashReconcileResult.Success(
            new CashReconcileResultDto(
                false,
                difference,
                ledger.CashAccount.Balance,
                Map(adjustment)));
    }

    private static List<string> ValidateBalance(decimal actualBalance)
    {
        var errors = new List<string>();

        if (actualBalance < 0)
        {
            errors.Add("核对后的现金余额不能为负数。");
        }
        else if (decimal.Round(actualBalance, 2) != actualBalance)
        {
            errors.Add("核对后的现金余额最多保留两位小数。");
        }
        else if (actualBalance > CashAccount.MaximumBalance)
        {
            errors.Add("核对后的现金余额超出允许范围。");
        }

        return errors;
    }

    private static List<string> ValidateReason(string? reason)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(reason))
        {
            errors.Add("调整原因不能为空。");
        }
        else if (reason.Trim().Length > CashAdjustment.MaximumReasonLength)
        {
            errors.Add($"调整原因不能超过 {CashAdjustment.MaximumReasonLength} 个字符。");
        }

        return errors;
    }

    private static IReadOnlyList<CashActivityDto> BuildActivities(
        IReadOnlyList<LedgerEntry> entries,
        IReadOnlyList<CashAdjustment> adjustments,
        IReadOnlyList<AssetTransaction> assetTransactions)
    {
        var activities = new List<(DateOnly BusinessDate, DateTimeOffset CreatedAt, CashActivityDto Activity)>();

        foreach (var entry in entries)
        {
            activities.Add((
                entry.BusinessDate,
                entry.CreatedAt,
                new CashActivityDto(
                    entry.Id,
                    CashActivityType.Daily,
                    entry.Category,
                    entry.Note,
                    entry.CashEffect,
                    entry.BusinessDate,
                    entry.Source,
                    null)));
        }

        foreach (var adjustment in adjustments)
        {
            activities.Add((
                adjustment.BusinessDate,
                adjustment.CreatedAt,
                new CashActivityDto(
                    adjustment.Id,
                    CashActivityType.Adjustment,
                    "余额调整",
                    adjustment.Reason,
                    adjustment.Difference,
                    adjustment.BusinessDate,
                    null,
                    null)));
        }

        foreach (var transaction in assetTransactions)
        {
            activities.Add((
                transaction.BusinessDate,
                transaction.CreatedAt,
                new CashActivityDto(
                    transaction.Id,
                    CashActivityType.AssetTransfer,
                    MapAssetTitle(transaction),
                    MapAssetNote(transaction),
                    transaction.CashEffect,
                    transaction.BusinessDate,
                    null,
                    transaction.AssetType)));
        }

        return activities
            .OrderByDescending(item => item.BusinessDate)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => item.Activity)
            .ToList();
    }

    private static string MapAssetTitle(AssetTransaction transaction)
    {
        return transaction.Kind switch
        {
            AssetTransactionKind.ProvidentWithdrawal => "公积金提取",
            AssetTransactionKind.FundBuy => "基金买入",
            AssetTransactionKind.FundSell => "基金卖出",
            AssetTransactionKind.GoldBuy => "积存金买入",
            AssetTransactionKind.GoldSell => "积存金卖出",
            _ => "资产流转"
        };
    }

    private static string? MapAssetNote(AssetTransaction transaction)
    {
        var parts = new[] { transaction.Subject, transaction.Note }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    private static CashAdjustmentDto Map(CashAdjustment adjustment)
    {
        return new CashAdjustmentDto(
            adjustment.Id,
            adjustment.BusinessDate,
            adjustment.PreviousBalance,
            adjustment.NewBalance,
            adjustment.Difference,
            adjustment.Reason,
            adjustment.CreatedAt);
    }
}
