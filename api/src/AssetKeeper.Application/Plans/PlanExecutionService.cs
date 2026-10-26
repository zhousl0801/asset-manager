using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Application.Provident;
using AssetKeeper.Domain.Ledgers;
using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Application.Plans;

public sealed class PlanExecutionService
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly ILedgerRepository _ledgerRepository;
    private readonly IPlanRepository _planRepository;
    private readonly BusinessDateProvider _dates;
    private readonly LedgerEntryService _entries;
    private readonly ProvidentFundService _provident;
    private readonly FundService _funds;

    public PlanExecutionService(
        ILedgerRepository ledgerRepository,
        IPlanRepository planRepository,
        BusinessDateProvider dates,
        LedgerEntryService entries,
        ProvidentFundService provident,
        FundService funds)
    {
        _ledgerRepository = ledgerRepository;
        _planRepository = planRepository;
        _dates = dates;
        _entries = entries;
        _provident = provident;
        _funds = funds;
    }

    public async Task<PlanRunResultDto> RunDueAsync(CancellationToken cancellationToken)
    {
        if (!await RunLock.WaitAsync(0, cancellationToken))
        {
            return new PlanRunResultDto(0, 0, 0, 0);
        }

        try
        {
            var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

            if (ledger is null)
            {
                return new PlanRunResultDto(0, 0, 0, 0);
            }

            var now = _dates.UtcNow;
            var today = _dates.Today;
            var plans = await _planRepository.GetPlansAsync(ledger.Id, cancellationToken);
            var activePlans = plans.Where(plan => plan.IsActive).ToList();
            var created = 0;

            foreach (var plan in activePlans)
            {
                foreach (var period in PlanSchedule.GetDuePeriods(plan, plan.GenerateFrom, today))
                {
                    if (_dates.ToBusinessInstant(period.DueDate, period.DueTime) > now)
                    {
                        continue;
                    }

                    if (await _planRepository.FindOccurrenceAsync(plan.Id, period.PeriodKey, cancellationToken) is not null)
                    {
                        continue;
                    }

                    await _planRepository.AddOccurrenceAsync(
                        PlanOccurrence.Create(plan, period.PeriodKey, period.DueDate, period.DueTime, now),
                        cancellationToken);
                    created++;
                }
            }

            if (created > 0)
            {
                await _planRepository.SaveChangesAsync(cancellationToken);
            }

            var activeIds = activePlans.Select(plan => plan.Id).ToHashSet();
            var pending = (await _planRepository.GetPendingOccurrencesAsync(ledger.Id, cancellationToken))
                .Where(occurrence => activeIds.Contains(occurrence.PlanId)
                    && _dates.ToBusinessInstant(occurrence.DueDate, occurrence.DueTime) <= now)
                .OrderBy(occurrence => occurrence.DueDate)
                .ThenBy(occurrence => occurrence.DueTime)
                .ThenBy(occurrence => PlanSchedule.TypeOrder(occurrence.Type))
                .ThenBy(occurrence => occurrence.PlanId)
                .ToList();
            var completed = 0;
            var waiting = 0;
            var failed = 0;

            foreach (var occurrence in pending)
            {
                var status = await ExecuteOccurrenceAsync(ledger.Id, occurrence, cancellationToken);

                if (status == PlanOccurrenceStatus.Completed)
                {
                    completed++;
                }
                else if (status == PlanOccurrenceStatus.Waiting)
                {
                    waiting++;
                }
                else if (status == PlanOccurrenceStatus.Failed)
                {
                    failed++;
                }
            }

            return new PlanRunResultDto(created, completed, waiting, failed);
        }
        finally
        {
            RunLock.Release();
        }
    }

    public Task<AssetOperationResult<RecordOccurrenceResultDto>> RecordOccurrenceAsync(
        Guid occurrenceId,
        PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.occurrence.record",
            request.RequestId,
            new { OccurrenceId = occurrenceId, Request = request },
            () => RecordOccurrenceCoreAsync(occurrenceId, cancellationToken),
            result => result.Status is AssetOperationStatus.Success,
            result => result,
            AssetOperationResult<RecordOccurrenceResultDto>.Conflict,
            message => AssetOperationResult<RecordOccurrenceResultDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<RecordOccurrenceResultDto>> RecordOccurrenceCoreAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        if (!await RunLock.WaitAsync(0, cancellationToken))
        {
            return AssetOperationResult<RecordOccurrenceResultDto>.Conflict("已有计划执行任务在运行，请稍后重试。");
        }

        try
        {
            var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

            if (ledger is null)
            {
                return AssetOperationResult<RecordOccurrenceResultDto>.NotFound("账本尚未初始化。");
            }

            var occurrence = await _planRepository.GetOccurrenceForUpdateAsync(
                ledger.Id,
                occurrenceId,
                cancellationToken);

            if (occurrence is null)
            {
                return AssetOperationResult<RecordOccurrenceResultDto>.NotFound("执行期次不存在。");
            }

            if (occurrence.Status is PlanOccurrenceStatus.Completed or PlanOccurrenceStatus.Skipped)
            {
                return AssetOperationResult<RecordOccurrenceResultDto>.Invalid(new[] { "已记账或已跳过的期次不能补记。" });
            }

            if (_dates.ToBusinessInstant(occurrence.DueDate, occurrence.DueTime) > _dates.UtcNow)
            {
                return AssetOperationResult<RecordOccurrenceResultDto>.Invalid(new[] { "该期次尚未到期，请等待自动执行。" });
            }

            // 补记沿用原期次的业务日期和金额；入账本身仍以期次标识作为 requestId 防重。
            var status = await ExecuteOccurrenceAsync(ledger.Id, occurrence, cancellationToken);
            var current = await _planRepository.GetOccurrenceForUpdateAsync(ledger.Id, occurrenceId, cancellationToken)
                ?? occurrence;
            var recorded = status == PlanOccurrenceStatus.Completed;

            return AssetOperationResult<RecordOccurrenceResultDto>.Success(new RecordOccurrenceResultDto(
                PlanService.Map(current),
                recorded,
                recorded ? null : current.ErrorMessage));
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<PlanOccurrenceStatus> ExecuteOccurrenceAsync(
        Guid ledgerId,
        PlanOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteBusinessAsync(occurrence, cancellationToken);
            var current = await _planRepository.GetOccurrenceForUpdateAsync(ledgerId, occurrence.Id, cancellationToken);

            if (current is null)
            {
                return PlanOccurrenceStatus.Pending;
            }

            if (result.Succeeded)
            {
                current.Complete(result.RelatedRecordId ?? occurrence.Id, _dates.UtcNow);
            }
            else if (result.Failed)
            {
                current.Fail(result.Error ?? "计划执行失败。", _dates.UtcNow);
            }
            else
            {
                current.Wait(result.Error ?? "余额或资料不足，等待处理。", _dates.UtcNow);
            }

            await _planRepository.SaveChangesAsync(cancellationToken);

            return current.Status;
        }
        catch (Exception exception)
        {
            try
            {
                var current = await _planRepository.GetOccurrenceForUpdateAsync(ledgerId, occurrence.Id, cancellationToken);

                if (current is not null)
                {
                    current.Fail(exception.Message, _dates.UtcNow);
                    await _planRepository.SaveChangesAsync(cancellationToken);

                    return PlanOccurrenceStatus.Failed;
                }
            }
            catch
            {
                // The background run must not stop because one occurrence could not be marked.
            }

            return PlanOccurrenceStatus.Failed;
        }
    }

    private async Task<ExecutionResult> ExecuteBusinessAsync(PlanOccurrence occurrence, CancellationToken cancellationToken)
    {
        switch (occurrence.Type)
        {
            case AutoPlanType.SalaryIncome:
            {
                var salaryCategory = await _ledgerRepository.GetCategoryBySystemKeyAsync(
                    occurrence.LedgerId,
                    LedgerEntryKind.Income,
                    "salary",
                    cancellationToken);
                var result = await _entries.CreateAsync(
                    new CreateLedgerEntryRequest(
                        LedgerEntryKind.Income,
                        salaryCategory?.Name ?? "工资",
                        occurrence.Amount,
                        occurrence.DueDate,
                        occurrence.PlanName,
                        occurrence.Id),
                    cancellationToken);

                if (result.Status is EntryOperationStatus.Created or EntryOperationStatus.Success)
                {
                    return ExecutionResult.Success(result.Result?.Entry.Id);
                }

                return result.Status == EntryOperationStatus.Conflict
                    ? ExecutionResult.Fail(Join(result.Errors))
                    : ExecutionResult.Wait(Join(result.Errors));
            }
            case AutoPlanType.ProvidentDeposit:
            {
                var result = await _provident.DepositAsync(
                    new ProvidentDepositRequest(occurrence.Amount, occurrence.DueDate, occurrence.PlanName, occurrence.Id),
                    cancellationToken);

                return FromAssetResult(result);
            }
            case AutoPlanType.ProvidentWithdrawal:
            {
                var result = await _provident.WithdrawAsync(
                    new ProvidentWithdrawalRequest(occurrence.Amount, occurrence.DueDate, occurrence.PlanName, occurrence.Id),
                    cancellationToken);

                return FromAssetResult(result);
            }
            default:
            {
                if (occurrence.FundPositionId is not Guid positionId)
                {
                    return ExecutionResult.Wait("基金定投缺少基金持仓。");
                }

                var result = await _funds.BuyAsync(
                    new BuyFundRequest(positionId, occurrence.Amount, occurrence.DueDate, occurrence.PlanName, occurrence.Id),
                    cancellationToken);

                return FromAssetResult(result);
            }
        }
    }

    private static ExecutionResult FromAssetResult<T>(AssetOperationResult<T> result)
        where T : class
    {
        if (result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success)
        {
            var id = result.Result switch
            {
                ProvidentMutationResultDto provident => provident.Activity.Id,
                FundMutationResultDto fund => fund.Activity.Id,
                _ => (Guid?)null
            };

            return ExecutionResult.Success(id);
        }

        return result.Status is AssetOperationStatus.Conflict or AssetOperationStatus.Unavailable
            ? ExecutionResult.Fail(Join(result.Errors))
            : ExecutionResult.Wait(Join(result.Errors));
    }

    private static string Join(IReadOnlyList<string> errors)
    {
        return errors.Count == 0 ? "计划执行失败。" : string.Join(" ", errors);
    }

    private sealed record ExecutionResult(bool Succeeded, bool Failed, Guid? RelatedRecordId, string? Error)
    {
        public static ExecutionResult Success(Guid? relatedRecordId)
        {
            return new ExecutionResult(true, false, relatedRecordId, null);
        }

        public static ExecutionResult Wait(string error)
        {
            return new ExecutionResult(false, false, null, error);
        }

        public static ExecutionResult Fail(string error)
        {
            return new ExecutionResult(false, true, null, error);
        }
    }
}
