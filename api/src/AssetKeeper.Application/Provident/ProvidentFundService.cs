using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.History;
using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Domain.Assets;

namespace AssetKeeper.Application.Provident;

public sealed class ProvidentFundService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly BusinessDateProvider _businessDateProvider;

    public ProvidentFundService(
        ILedgerRepository ledgerRepository,
        BusinessDateProvider businessDateProvider)
    {
        _ledgerRepository = ledgerRepository;
        _businessDateProvider = businessDateProvider;
    }

    public async Task<ProvidentOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var account = await _ledgerRepository.GetProvidentFundAccountAsync(ledger.Id, cancellationToken);
        var (from, to) = EntryPeriodRange.For(EntryPeriod.Month, _businessDateProvider.Today);
        var transactions = await _ledgerRepository.GetAssetTransactionsAsync(
            ledger.Id,
            AssetType.ProvidentFund,
            from,
            to,
            int.MaxValue,
            cancellationToken);
        var monthDeposit = transactions
            .Where(transaction => transaction.Kind == AssetTransactionKind.ProvidentDeposit)
            .Sum(transaction => transaction.Amount);
        var monthWithdrawal = transactions
            .Where(transaction => transaction.Kind == AssetTransactionKind.ProvidentWithdrawal)
            .Sum(transaction => transaction.Amount);
        var openingBalance = (account?.Balance ?? 0m) - monthDeposit + monthWithdrawal
            - transactions.Where(transaction => transaction.Kind == AssetTransactionKind.ProvidentOpening).Sum(transaction => transaction.Amount);
        var balanceAfterByTransaction = BuildBalanceAfter(transactions, openingBalance);
        var activities = transactions
            .Select(transaction => Map(transaction, balanceAfterByTransaction[transaction.Id]))
            .ToList();

        return new ProvidentOverviewDto(
            account is not null,
            account?.Balance ?? 0m,
            account?.AsOfDate ?? ledger.StartDate,
            monthDeposit,
            monthWithdrawal,
            monthDeposit - monthWithdrawal,
            activities,
            from,
            to);
    }

    public Task<AssetOperationResult<ProvidentMutationResultDto>> SetOpeningAsync(ProvidentOpeningRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync("provident.opening", request.RequestId, request,
            async () =>
            {
                var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

                if (ledger is null)
                {
                    return AssetOperationResult<ProvidentMutationResultDto>.NotFound("账本尚未初始化。");
                }

                var data = await _ledgerRepository.GetReplayDataAsync(ledger.Id, cancellationToken);

                if (data.Transactions.Any(item => !item.IsReversed && item.Kind == AssetTransactionKind.ProvidentOpening))
                {
                    return AssetOperationResult<ProvidentMutationResultDto>.Conflict("已有起始公积金，请在历史核对中更正。");
                }

                var errors = AssetValidation.ValidateBusinessDate(request.BusinessDate, ledger.StartDate, _businessDateProvider.Today);

                if (errors.Count > 0)
                {
                    return AssetOperationResult<ProvidentMutationResultDto>.Invalid(errors);
                }

                try
                {
                    var account = data.Provident;

                    if (account is null)
                    {
                        account = ProvidentFundAccount.Create(ledger.Id, request.BusinessDate, _businessDateProvider.UtcNow);
                        await _ledgerRepository.AddProvidentFundAccountAsync(account, cancellationToken);
                    }

                    var transaction = AssetTransaction.CreateSnapshot(ledger.Id, null, AssetType.ProvidentFund, AssetTransactionKind.ProvidentOpening,
                        null, request.Balance, null, request.BusinessDate, null, request.Note, request.RequestId, _businessDateProvider.UtcNow);
                    await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, null, null, cancellationToken);
                    return AssetOperationResult<ProvidentMutationResultDto>.Created(new(account.Balance, Map(transaction, account.Balance)));
                }
                catch (ArgumentException exception)
                {
                    return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { exception.Message });
                }
            }, result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success }, AssetOperationResult<ProvidentMutationResultDto>.Conflict,
            message => AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { message }), cancellationToken);
    }

    public Task<AssetOperationResult<ProvidentMutationResultDto>> DepositAsync(ProvidentDepositRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "provident.deposit", request.RequestId, request,
            () => DepositCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<ProvidentMutationResultDto>.Conflict,
            message => AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<ProvidentMutationResultDto>> DepositCoreAsync(
        ProvidentDepositRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(validationErrors);
        }


        try
        {
            var now = _businessDateProvider.UtcNow;
            var account = await _ledgerRepository.GetProvidentFundAccountForUpdateAsync(
                ledger.Id,
                cancellationToken);
            var isNewAccount = account is null;
            account ??= ProvidentFundAccount.Create(ledger.Id, request.BusinessDate, now);
            var transaction = AssetTransaction.CreateProvidentDeposit(
                ledger.Id,
                request.Amount,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);


            if (isNewAccount)
            {
                await _ledgerRepository.AddProvidentFundAccountAsync(account, cancellationToken);
            }

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, null, null, cancellationToken);


            return AssetOperationResult<ProvidentMutationResultDto>.Created(
                new ProvidentMutationResultDto(account.Balance, Map(transaction, account.Balance)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<ProvidentMutationResultDto>> WithdrawAsync(ProvidentWithdrawalRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _businessDateProvider).ExecuteAsync(
            "provident.withdraw", request.RequestId, request,
            () => WithdrawCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<ProvidentMutationResultDto>.Conflict,
            message => AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<ProvidentMutationResultDto>> WithdrawCoreAsync(
        ProvidentWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.NotFound("账本尚未初始化。");
        }

        if (ledger.CashAccount is null)
        {
            throw new InvalidOperationException("账本缺少现金账户。");
        }

        if (request.RequestId == Guid.Empty)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { "请求标识无效。" });
        }

        var validationErrors = AssetValidation.ValidateBusinessDate(
            request.BusinessDate,
            ledger.StartDate,
            _businessDateProvider.Today);

        if (validationErrors.Count > 0)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(validationErrors);
        }

        var account = await _ledgerRepository.GetProvidentFundAccountForUpdateAsync(
            ledger.Id,
            cancellationToken);

        if (account is null)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { "公积金余额不足。" });
        }



        try
        {
            var now = _businessDateProvider.UtcNow;
            var transaction = AssetTransaction.CreateProvidentWithdrawal(
                ledger.Id,
                request.Amount,
                request.BusinessDate,
                request.Note,
                request.RequestId,
                now);

            await new LedgerHistoryRebuilder(_ledgerRepository, _businessDateProvider).StageAsync(ledger, transaction, request.BusinessDate, null, request.RelatedCashAdjustmentId, cancellationToken);


            return AssetOperationResult<ProvidentMutationResultDto>.Created(
                new ProvidentMutationResultDto(account.Balance, Map(transaction, account.Balance)));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<ProvidentMutationResultDto>.Invalid(new[] { exception.Message });
        }
    }

    private static Dictionary<Guid, decimal> BuildBalanceAfter(
        IReadOnlyList<AssetTransaction> transactions,
        decimal openingBalance)
    {
        var result = new Dictionary<Guid, decimal>();
        var balance = openingBalance;

        foreach (var transaction in transactions
            .OrderBy(transaction => transaction.BusinessDate)
            .ThenBy(transaction => transaction.BusinessSequence))
        {
            balance += transaction.Kind is AssetTransactionKind.ProvidentDeposit or AssetTransactionKind.ProvidentOpening
                ? transaction.Amount
                : -transaction.Amount;
            result[transaction.Id] = balance;
        }

        return result;
    }

    private static ProvidentActivityDto Map(AssetTransaction transaction, decimal balanceAfter)
    {
        var type = transaction.Kind switch
        {
            AssetTransactionKind.ProvidentOpening => ProvidentActivityType.Opening,
            AssetTransactionKind.ProvidentDeposit => ProvidentActivityType.Deposit,
            _ => ProvidentActivityType.Withdrawal
        };

        return new ProvidentActivityDto(
            transaction.Id,
            type,
            transaction.Amount,
            balanceAfter,
            transaction.BusinessDate,
            transaction.Note,
            transaction.CreatedAt);
    }
}
