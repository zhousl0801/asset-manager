using AssetKeeper.Application.Assets;
using AssetKeeper.Application.Funds;
using AssetKeeper.Application.Ledgers;
using AssetKeeper.Application.Mutations;
using AssetKeeper.Domain.Assets;
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
        var mappedPlans = plans.Select(plan => Map(plan, positionNames, today, positions)).ToList();
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

    public PlanCalendarDto GetCalendar(int year, int month)
    {
        return new PlanCalendarDto(year, month, TradingCalendar.Default.GetMonth(year, month));
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
        }

        var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
        string? positionName = null;

        if (request.Type == AutoPlanType.FundSip)
        {
            FundPosition? position;

            if (request.FundTarget is { } target)
            {
                var targetKey = FundIdentity.ClassKey(target.Name, target.Code, target.ShareClass);
                position = positions.Where(item => FundIdentity.ClassKey(item) == targetKey).OrderBy(item => item.Id).FirstOrDefault();
            }
            else
            {
                position = positions.FirstOrDefault(item => item.Id == request.FundPositionId);
            }

            if (position is null)
            {
                return AssetOperationResult<PlanDto>.Invalid(["请选择已有基金及份额类别。"]);
            }

            request = request with { FundPositionId = position.Id };
            positionName = position.DisplayName;

            if ((plan is null || plan.IsActive) && plans.Any(item => item.Id != request.Id && IsSameSip(item, request, positions)))
            {
                return AssetOperationResult<PlanDto>.Conflict("已存在相同基金类别、金额、周期和时间的有效定投计划。");
            }
        }

        if (plan is null && request.Cycle == AutoPlanCycle.TradingDay)
        {
            var earliest = request.StartDate > _dates.Today ? request.StartDate.AddDays(-1) : _dates.Today;
            request = request with { StartDate = TradingCalendar.Default.NextTradingDay(earliest) };
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
                var generationFloor = plan.Cycle == AutoPlanCycle.TradingDay
                    && _dates.ToBusinessInstant(_dates.Today, request.Time) > now ? _dates.Today : _dates.Today.AddDays(1);
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
                    now,
                    generationFloor);
            }

            var positionNames = new Dictionary<Guid, string>();

            if (request.FundPositionId is Guid id && positionName is not null)
            {
                positionNames[id] = positionName;
            }

            return plan.Version == 0
                ? AssetOperationResult<PlanDto>.Created(Map(plan, positionNames, _dates.Today, positions))
                : AssetOperationResult<PlanDto>.Success(Map(plan, positionNames, _dates.Today, positions));
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

            if (plan.IsActive && plan.Type == AutoPlanType.FundSip)
            {
                var plans = await _planRepository.GetPlansAsync(ledger.Id, cancellationToken);
                var positions = await _ledgerRepository.GetFundPositionsAsync(ledger.Id, cancellationToken);
                var target = new SavePlanRequest(plan.Id, plan.Type, plan.Name, plan.Amount, plan.Cycle,
                    plan.DayOfMonth, plan.DayOfWeek, plan.Time, plan.StartDate, plan.FundPositionId, Guid.Empty, null);

                if (plans.Any(item => item.Id != plan.Id && IsSameSip(item, target, positions)))
                {
                    return AssetOperationResult<PlanDto>.Conflict("已存在相同基金类别、金额、周期和时间的有效定投计划。");
                }
            }
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

    private static bool IsSameSip(AutoPlan plan, SavePlanRequest request, IReadOnlyList<FundPosition> positions)
    {
        var existing = positions.FirstOrDefault(position => position.Id == plan.FundPositionId);
        var requested = positions.FirstOrDefault(position => position.Id == request.FundPositionId);

        return plan.Type == AutoPlanType.FundSip
            && existing is not null && requested is not null
            && FundIdentity.ClassKey(existing) == FundIdentity.ClassKey(requested)
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

    private PlanDto Map(
        AutoPlan plan,
        IReadOnlyDictionary<Guid, string> positionNames,
        DateOnly today,
        IReadOnlyList<FundPosition>? positions = null)
    {
        string? positionName = plan.FundPositionId is Guid id ? positionNames.GetValueOrDefault(id) : null;
        var from = _dates.ToBusinessInstant(today, plan.Time) <= _dates.UtcNow ? today.AddDays(1) : today;
        var next = plan.IsActive ? PlanSchedule.GetNextDue(plan, from) : null;
        var position = positions?.FirstOrDefault(item => item.Id == plan.FundPositionId);

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
            plan.Version,
            position is null ? null : new FundPlanTarget(position.Name, position.Code, position.ShareClass),
            plan.IsActive ? PlanSchedule.GetDuePeriods(plan, from, from.AddDays(90)).Take(10).Select(period => period.DueDate).ToList() : []);
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
