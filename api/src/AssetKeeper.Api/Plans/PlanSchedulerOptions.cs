namespace AssetKeeper.Api.Plans;

public sealed class PlanSchedulerOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalSeconds { get; set; } = 60;
}
