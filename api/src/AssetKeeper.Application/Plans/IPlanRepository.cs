using AssetKeeper.Domain.Plans;

namespace AssetKeeper.Application.Plans;

public interface IPlanRepository
{
    Task<IReadOnlyList<AutoPlan>> GetPlansAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<AutoPlan?> GetPlanForUpdateAsync(Guid ledgerId, Guid planId, CancellationToken cancellationToken);

    Task AddPlanAsync(AutoPlan plan, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanOccurrence>> GetOccurrencesAsync(
        Guid ledgerId,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanOccurrence>> GetPendingOccurrencesAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlanOccurrence>> GetActionableOccurrencesAsync(Guid ledgerId, CancellationToken cancellationToken);

    Task<PlanOccurrence?> GetOccurrenceForUpdateAsync(Guid ledgerId, Guid occurrenceId, CancellationToken cancellationToken);

    Task<PlanOccurrence?> FindOccurrenceAsync(Guid planId, string periodKey, CancellationToken cancellationToken);

    Task AddOccurrenceAsync(PlanOccurrence occurrence, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
