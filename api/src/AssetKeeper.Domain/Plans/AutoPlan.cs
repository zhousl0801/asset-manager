namespace AssetKeeper.Domain.Plans;

public sealed class AutoPlan
{
    public const decimal MaximumAmount = 1_000_000_000m;
    public const int MaximumNameLength = 50;

    private AutoPlan()
    {
    }

    public Guid Id { get; private set; }

    public Guid LedgerId { get; private set; }

    public AutoPlanType Type { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }

    public AutoPlanCycle Cycle { get; private set; }

    public int? DayOfMonth { get; private set; }

    public int? DayOfWeek { get; private set; }

    public TimeOnly Time { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly GenerateFrom { get; private set; }

    public bool IsActive { get; private set; }

    public Guid? FundPositionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; }

    public static AutoPlan Create(
        Guid ledgerId,
        AutoPlanType type,
        string name,
        decimal amount,
        AutoPlanCycle cycle,
        int? dayOfMonth,
        int? dayOfWeek,
        TimeOnly time,
        DateOnly startDate,
        Guid? fundPositionId,
        DateTimeOffset now)
    {
        var plan = new AutoPlan
        {
            Id = Guid.NewGuid(),
            LedgerId = ledgerId,
            IsActive = true,
            CreatedAt = now
        };
        plan.SetValues(type, name, amount, cycle, dayOfMonth, dayOfWeek, time, startDate, fundPositionId, now);

        return plan;
    }

    public void Update(
        AutoPlanType type,
        string name,
        decimal amount,
        AutoPlanCycle cycle,
        int? dayOfMonth,
        int? dayOfWeek,
        TimeOnly time,
        DateOnly startDate,
        Guid? fundPositionId,
        DateTimeOffset now,
        DateOnly generationFloor)
    {
        if (type != Type)
        {
            throw new ArgumentException("计划类型不能修改，请新建对应计划。");
        }

        SetValues(type, name, amount, cycle, dayOfMonth, dayOfWeek, time, startDate, fundPositionId, now);
        GenerateFrom = GenerateFrom > startDate ? GenerateFrom : startDate;
        if (cycle == AutoPlanCycle.TradingDay && GenerateFrom < generationFloor)
        {
            GenerateFrom = generationFloor;
        }

        UpdatedAt = now;
        Version++;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("计划已暂停。");
        }

        IsActive = false;
        UpdatedAt = now;
        Version++;
    }

    public void Resume(DateOnly businessToday, DateTimeOffset now)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("计划已启用。");
        }

        IsActive = true;
        var nextDate = businessToday.AddDays(1);

        if (nextDate > GenerateFrom)
        {
            GenerateFrom = nextDate;
        }

        UpdatedAt = now;
        Version++;
    }

    private void SetValues(
        AutoPlanType type,
        string name,
        decimal amount,
        AutoPlanCycle cycle,
        int? dayOfMonth,
        int? dayOfWeek,
        TimeOnly time,
        DateOnly startDate,
        Guid? fundPositionId,
        DateTimeOffset now)
    {
        if (LedgerId == Guid.Empty)
        {
            throw new ArgumentException("账本标识无效。", nameof(LedgerId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
        {
            throw new ArgumentException($"计划名称不能为空且不能超过 {MaximumNameLength} 个字符。", nameof(name));
        }

        if (amount <= 0 || amount > MaximumAmount || decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentException("计划金额必须大于 0，最多两位小数。", nameof(amount));
        }

        if (startDate == default)
        {
            throw new ArgumentException("计划开始日期无效。", nameof(startDate));
        }

        if (type != AutoPlanType.FundSip && cycle != AutoPlanCycle.Monthly)
        {
            throw new ArgumentException("工资、缴存和提取计划仅支持每月周期。", nameof(cycle));
        }

        if (!Enum.IsDefined(type) || !Enum.IsDefined(cycle))
        {
            throw new ArgumentException("计划类型或周期无效。");
        }

        if (cycle == AutoPlanCycle.TradingDay)
        {
            if (dayOfMonth is not null || dayOfWeek is not null)
            {
                throw new ArgumentException("交易日计划不填写每月日期或星期。");
            }
        }
        else if (cycle == AutoPlanCycle.Monthly)
        {
            if (dayOfMonth is null || dayOfMonth < 1 || dayOfMonth > 31 || dayOfWeek is not null)
            {
                throw new ArgumentException("每月计划须填写 1 至 31 的日期。", nameof(dayOfMonth));
            }
        }
        else if (dayOfWeek is null || dayOfWeek < 1 || dayOfWeek > 7 || dayOfMonth is not null)
        {
            throw new ArgumentException("每周计划须选择星期。", nameof(dayOfWeek));
        }

        if (type == AutoPlanType.FundSip && (fundPositionId is null || fundPositionId == Guid.Empty))
        {
            throw new ArgumentException("基金定投必须选择基金持仓。", nameof(fundPositionId));
        }

        if (type != AutoPlanType.FundSip && fundPositionId is not null)
        {
            throw new ArgumentException("只有基金定投可以关联基金持仓。", nameof(fundPositionId));
        }

        Type = type;
        Name = name.Trim();
        Amount = amount;
        Cycle = cycle;
        DayOfMonth = dayOfMonth;
        DayOfWeek = dayOfWeek;
        Time = time;
        StartDate = startDate;
        FundPositionId = fundPositionId;

        if (GenerateFrom == default)
        {
            GenerateFrom = startDate;
        }

        UpdatedAt = now;
    }
}
