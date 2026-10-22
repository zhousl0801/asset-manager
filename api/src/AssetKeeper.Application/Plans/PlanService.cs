using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Application.Plans;

public sealed class PlanService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly IPlanRepository _planRepository;
    private readonly BusinessDateProvider _dates;

    public PlanService(
        ILedgerRepository ledgerRepository,
        IPlanRepository planRepository,
        BusinessDateProvider dates)
    {
        _ledgerRepository = ledgerRepository;
        _planRepository = planRepository;
        _dates = dates;
    }

    public async Task<PlanOverviewDto?> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return null;
        }

        var plans = await _planRepository.GetPlansAsync(ledger.Id, cancellationToken);
        var pending = await _planRepository.GetActionableOccurrencesAsync(ledger.Id, cancellationToken);
        var recent = await _planRepository.GetOccurrencesAsync(ledger.Id, null, null, 100, cancellationToken);
        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        var positionNames = positions.ToDictionary(position => position.Id, position => position.DisplayName);
        var provident = await _ledgerRepository.GetProvidentFundAccountAsync(ledger.Id, cancellationToken);
        var today = _dates.Today;
        var mappedPlans = plans.Select(plan => Map(plan, positionNames, today)).ToList();
        var nextDue = mappedPlans.Where(plan => plan.IsActive && plan.NextDueDate is not null)
            .Select(plan => plan.NextDueDate)
            .Min();

        return new PlanOverviewDto(
            mappedPlans,
            pending.Select(Map).ToList(),
            recent.Select(Map).ToList(),
            mappedPlans.Count(plan => plan.IsActive),
            nextDue,
            ledger.CashAccount?.Balance ?? 0m,
            provident?.Balance ?? 0m);
    }

    public Task<AssetOperationResult<PlanDto>> SaveAsync(SavePlanRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.save",
            request.RequestId,
            request,
            () => SaveCoreAsync(request, cancellationToken),
            result => result.Status is AssetOperationStatus.Created or AssetOperationStatus.Success,
            result => result with { Status = AssetOperationStatus.Success },
            AssetOperationResult<PlanDto>.Conflict,
            message => AssetOperationResult<PlanDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<PlanDto>> SaveCoreAsync(SavePlanRequest request, CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<PlanDto>.NotFound("账本尚未初始化。");
        }

        if (request.StartDate < ledger.StartDate)
        {
            return AssetOperationResult<PlanDto>.Invalid(new[] { "计划开始日期不能早于账本起始日期。" });
        }

        var plans = await _planRepository.GetPlansAsync(ledger.Id, cancellationToken);
        AutoPlan? plan = null;

        if (request.Id is Guid planId)
        {
            plan = await _planRepository.GetPlanForUpdateAsync(ledger.Id, planId, cancellationToken);

            if (plan is null)
            {
                return AssetOperationResult<PlanDto>.NotFound("计划不存在。");
            }

            if (request.ExpectedVersion is not null && request.ExpectedVersion != plan.Version)
            {
                return AssetOperationResult<PlanDto>.Conflict("计划已变化，请刷新后重试。");
            }
        }
        else
        {
            if (request.Type != AutoPlanType.FundSip && plans.Any(item => item.Type == request.Type))
            {
                return AssetOperationResult<PlanDto>.Invalid(new[] { "该类型已有主计划，请编辑现有计划。" });
            }

            if (request.Type == AutoPlanType.FundSip && plans.Any(item => IsSameSip(item, request)))
            {
                return AssetOperationResult<PlanDto>.Invalid(new[] { "已存在相同基金、金额和周期的定投计划，请核对。" });
            }
        }

        string? positionName = null;

        if (request.Type == AutoPlanType.FundSip)
        {
            if (request.FundPositionId is not Guid positionId)
            {
                return AssetOperationResult<PlanDto>.Invalid(new[] { "基金定投必须选择基金持仓。" });
            }

            var position = await _ledgerRepository.GetFundPositionForUpdateAsync(ledger.Id, positionId, cancellationToken);

            if (position is null)
            {
                return AssetOperationResult<PlanDto>.NotFound("基金持仓不存在。");
            }

            positionName = position.DisplayName;
        }

        var name = string.IsNullOrWhiteSpace(request.Name) ? DefaultName(request.Type, positionName) : request.Name.Trim();
        var now = _dates.UtcNow;

        try
        {
            if (plan is null)
            {
                plan = AutoPlan.Create(
                    ledger.Id,
                    request.Type,
                    name,
                    request.Amount,
                    request.Cycle,
                    request.DayOfMonth,
                    request.DayOfWeek,
                    request.Time,
                    request.StartDate,
                    request.FundPositionId,
                    now);
                await _planRepository.AddPlanAsync(plan, cancellationToken);
            }
            else
            {
                plan.Update(
                    request.Type,
                    name,
                    request.Amount,
                    request.Cycle,
                    request.DayOfMonth,
                    request.DayOfWeek,
                    request.Time,
                    request.StartDate,
                    request.FundPositionId,
                    now);
            }

            var positionNames = new Dictionary<Guid, string>();

            if (request.FundPositionId is Guid id && positionName is not null)
            {
                positionNames[id] = positionName;
            }

            return plan.Version == 0
                ? AssetOperationResult<PlanDto>.Created(Map(plan, positionNames, _dates.Today))
                : AssetOperationResult<PlanDto>.Success(Map(plan, positionNames, _dates.Today));
        }
        catch (ArgumentException exception)
        {
            return AssetOperationResult<PlanDto>.Invalid(new[] { exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<PlanDto>.Invalid(new[] { exception.Message });
        }
    }

    public Task<AssetOperationResult<PlanDto>> PauseAsync(Guid planId, PlanActionRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.pause",
            request.RequestId,
            new { PlanId = planId, Request = request },
            () => TransitionAsync(planId, plan => plan.Pause(_dates.UtcNow), cancellationToken),
            result => result.Status is AssetOperationStatus.Success,
            result => result,
            AssetOperationResult<PlanDto>.Conflict,
            message => AssetOperationResult<PlanDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    public Task<AssetOperationResult<PlanDto>> ResumeAsync(Guid planId, PlanActionRequest request, CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.resume",
            request.RequestId,
            new { PlanId = planId, Request = request },
            () => TransitionAsync(planId, plan => plan.Resume(_dates.Today, _dates.UtcNow), cancellationToken),
            result => result.Status is AssetOperationStatus.Success,
            result => result,
            AssetOperationResult<PlanDto>.Conflict,
            message => AssetOperationResult<PlanDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<PlanDto>> TransitionAsync(
        Guid planId,
        Action<AutoPlan> transition,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<PlanDto>.NotFound("账本尚未初始化。");
        }

        var plan = await _planRepository.GetPlanForUpdateAsync(ledger.Id, planId, cancellationToken);

        if (plan is null)
        {
            return AssetOperationResult<PlanDto>.NotFound("计划不存在。");
        }

        try
        {
            transition(plan);
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<PlanDto>.Invalid(new[] { exception.Message });
        }

        return AssetOperationResult<PlanDto>.Success(Map(plan, new Dictionary<Guid, string>(), _dates.Today));
    }

    public Task<AssetOperationResult<PlanOccurrenceDto>> SkipOccurrenceAsync(
        Guid occurrenceId,
        PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.occurrence.skip",
            request.RequestId,
            new { OccurrenceId = occurrenceId, Request = request },
            () => OccurrenceTransitionAsync(occurrenceId, occurrence => occurrence.Skip(_dates.UtcNow), cancellationToken),
            result => result.Status is AssetOperationStatus.Success,
            result => result,
            AssetOperationResult<PlanOccurrenceDto>.Conflict,
            message => AssetOperationResult<PlanOccurrenceDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    public Task<AssetOperationResult<PlanOccurrenceDto>> RetryOccurrenceAsync(
        Guid occurrenceId,
        PlanActionRequest request,
        CancellationToken cancellationToken)
    {
        return new MutationExecutor(_ledgerRepository, _dates).ExecuteAsync(
            "plan.occurrence.retry",
            request.RequestId,
            new { OccurrenceId = occurrenceId, Request = request },
            () => OccurrenceTransitionAsync(occurrenceId, occurrence => occurrence.Retry(_dates.UtcNow), cancellationToken),
            result => result.Status is AssetOperationStatus.Success,
            result => result,
            AssetOperationResult<PlanOccurrenceDto>.Conflict,
            message => AssetOperationResult<PlanOccurrenceDto>.Invalid(new[] { message }),
            cancellationToken);
    }

    private async Task<AssetOperationResult<PlanOccurrenceDto>> OccurrenceTransitionAsync(
        Guid occurrenceId,
        Action<PlanOccurrence> transition,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetForUpdateAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<PlanOccurrenceDto>.NotFound("账本尚未初始化。");
        }

        var occurrence = await _planRepository.GetOccurrenceForUpdateAsync(ledger.Id, occurrenceId, cancellationToken);

        if (occurrence is null)
        {
            return AssetOperationResult<PlanOccurrenceDto>.NotFound("执行期次不存在。");
        }

        try
        {
            transition(occurrence);

            return AssetOperationResult<PlanOccurrenceDto>.Success(Map(occurrence));
        }
        catch (InvalidOperationException exception)
        {
            return AssetOperationResult<PlanOccurrenceDto>.Invalid(new[] { exception.Message });
        }
    }

    public async Task<AssetOperationResult<PlanPreviewDto>> PreviewAsync(
        PlanPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await _ledgerRepository.GetAsync(cancellationToken);

        if (ledger is null)
        {
            return AssetOperationResult<PlanPreviewDto>.NotFound("账本尚未初始化。");
        }

        if (request.Date < _dates.Today)
        {
            return AssetOperationResult<PlanPreviewDto>.Invalid(new[] { "预览日期不能早于今天。" });
        }

        var plans = await _planRepository.GetPlansAsync(ledger.Id, cancellationToken);
        var provident = await _ledgerRepository.GetProvidentFundAccountAsync(ledger.Id, cancellationToken);
        var cash = request.TrialCash ?? ledger.CashAccount?.Balance ?? 0m;
        var providentBalance = provident?.Balance ?? 0m;
        var startCash = cash;
        var startProvident = providentBalance;
        var rows = new List<PlanPreviewRow>();

        foreach (var plan in plans.Where(plan => PlanSchedule.IsDueOn(plan, request.Date))
            .OrderBy(plan => plan.Time)
            .ThenBy(plan => PlanSchedule.TypeOrder(plan.Type))
            .ThenBy(plan => plan.Id))
        {
            var periodKey = PlanSchedule.PeriodKeyFor(plan, request.Date);
            var occurrence = await _planRepository.FindOccurrenceAsync(plan.Id, periodKey, cancellationToken);

            if (!plan.IsActive && occurrence is null)
            {
                continue;
            }

            var status = occurrence?.Status;
            var note = occurrence?.ErrorMessage;

            if (status is PlanOccurrenceStatus.Completed or PlanOccurrenceStatus.Skipped
                or PlanOccurrenceStatus.Waiting or PlanOccurrenceStatus.Failed)
            {
                rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount,
                    StatusText(status.Value), cash, providentBalance, note));
                continue;
            }

            switch (plan.Type)
            {
                case AutoPlanType.SalaryIncome:
                    cash += plan.Amount;
                    rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "预计记账", cash, providentBalance, null));
                    break;
                case AutoPlanType.ProvidentDeposit:
                    providentBalance += plan.Amount;
                    rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "预计记账", cash, providentBalance, null));
                    break;
                case AutoPlanType.ProvidentWithdrawal:
                    if (providentBalance < plan.Amount)
                    {
                        rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "公积金不足", cash, providentBalance, "整笔待处理，不部分提取"));
                    }
                    else
                    {
                        providentBalance -= plan.Amount;
                        cash += plan.Amount;
                        rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "预计记账", cash, providentBalance, null));
                    }

                    break;
                default:
                    if (cash < plan.Amount)
                    {
                        rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "现金不足", cash, providentBalance, "整笔待处理，不部分扣款"));
                    }
                    else
                    {
                        cash -= plan.Amount;
                        rows.Add(new PlanPreviewRow(plan.Id, plan.Name, plan.Type, plan.Amount, "预计记账", cash, providentBalance, null));
                    }

                    break;
            }
        }

        return AssetOperationResult<PlanPreviewDto>.Success(new PlanPreviewDto(
            request.Date,
            startCash,
            cash,
            startProvident,
            providentBalance,
            rows,
            new[] { "仅试算所选日，不含中间日期的其他收支、行情或资金变动。", "计划只生成账本记录，不验证银行或平台的实际资金操作。" }));
    }

    private static bool IsSameSip(AutoPlan plan, SavePlanRequest request)
    {
        return plan.Type == AutoPlanType.FundSip
            && plan.FundPositionId == request.FundPositionId
            && plan.Amount == request.Amount
            && plan.Cycle == request.Cycle
            && plan.DayOfMonth == request.DayOfMonth
            && plan.DayOfWeek == request.DayOfWeek
            && plan.Time == request.Time
            && plan.IsActive;
    }

    private static string DefaultName(AutoPlanType type, string? positionName)
    {
        return type switch
        {
            AutoPlanType.SalaryIncome => "工资到账",
            AutoPlanType.ProvidentDeposit => "公积金缴存",
            AutoPlanType.ProvidentWithdrawal => "公积金提取",
            _ => $"基金定投 · {positionName ?? "基金"}"
        };
    }

    private static string StatusText(PlanOccurrenceStatus status)
    {
        return status switch
        {
            PlanOccurrenceStatus.Completed => "已记账",
            PlanOccurrenceStatus.Skipped => "已跳过",
            PlanOccurrenceStatus.Waiting => "待处理",
            PlanOccurrenceStatus.Failed => "执行失败",
            _ => "待执行"
        };
    }

    private static PlanDto Map(
        AutoPlan plan,
        IReadOnlyDictionary<Guid, string> positionNames,
        DateOnly today)
    {
        string? positionName = plan.FundPositionId is Guid id ? positionNames.GetValueOrDefault(id) : null;
        var next = plan.IsActive ? PlanSchedule.GetNextDue(plan, today) : null;

        return new PlanDto(
            plan.Id,
            plan.Type,
            plan.Name,
            plan.Amount,
            plan.Cycle,
            plan.DayOfMonth,
            plan.DayOfWeek,
            plan.Time,
            plan.StartDate,
            plan.GenerateFrom,
            plan.IsActive,
            plan.FundPositionId,
            positionName,
            next?.DueDate,
            plan.Version);
    }

    public static PlanOccurrenceDto Map(PlanOccurrence occurrence)
    {
        return new PlanOccurrenceDto(
            occurrence.Id,
            occurrence.PlanId,
            occurrence.PlanName,
            occurrence.Type,
            occurrence.Amount,
            occurrence.DueDate,
            occurrence.DueTime,
            occurrence.Status,
            occurrence.RelatedRecordId,
            occurrence.ErrorMessage,
            occurrence.ExecutedAt,
            occurrence.PeriodKey,
            occurrence.PlanVersion);
    }
}
