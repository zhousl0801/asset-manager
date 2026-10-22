using AssetKeeper.Application.Plans;
using AssetKeeper.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace AssetKeeper.Infrastructure.Persistence;

public sealed class PlanRepository : IPlanRepository
{
    private readonly AppDbContext _dbContext;

    public PlanRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AutoPlan>> GetPlansAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return await _dbContext.AutoPlans
            .AsNoTracking()
            .Where(plan => plan.LedgerId == ledgerId)
            .OrderBy(plan => plan.Type)
            .ThenBy(plan => plan.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<AutoPlan?> GetPlanForUpdateAsync(Guid ledgerId, Guid planId, CancellationToken cancellationToken)
    {
        return _dbContext.AutoPlans
            .SingleOrDefaultAsync(plan => plan.Id == planId && plan.LedgerId == ledgerId, cancellationToken);
    }

    public async Task AddPlanAsync(AutoPlan plan, CancellationToken cancellationToken)
    {
        await _dbContext.AutoPlans.AddAsync(plan, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanOccurrence>> GetOccurrencesAsync(
        Guid ledgerId,
        DateOnly? from,
        DateOnly? to,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.PlanOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.LedgerId == ledgerId);

        if (from is not null)
        {
            query = query.Where(occurrence => occurrence.DueDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(occurrence => occurrence.DueDate <= to);
        }

        return await query
            .OrderByDescending(occurrence => occurrence.DueDate)
            .ThenByDescending(occurrence => occurrence.DueTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlanOccurrence>> GetPendingOccurrencesAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return await _dbContext.PlanOccurrences
            .Where(occurrence => occurrence.LedgerId == ledgerId && occurrence.Status == PlanOccurrenceStatus.Pending)
            .OrderBy(occurrence => occurrence.DueDate)
            .ThenBy(occurrence => occurrence.DueTime)
            .ThenBy(occurrence => occurrence.Type)
            .ThenBy(occurrence => occurrence.PlanId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlanOccurrence>> GetActionableOccurrencesAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        return await _dbContext.PlanOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.LedgerId == ledgerId
                && (occurrence.Status == PlanOccurrenceStatus.Pending
                    || occurrence.Status == PlanOccurrenceStatus.Waiting
                    || occurrence.Status == PlanOccurrenceStatus.Failed))
            .OrderBy(occurrence => occurrence.DueDate)
            .ThenBy(occurrence => occurrence.DueTime)
            .ToListAsync(cancellationToken);
    }

    public Task<PlanOccurrence?> GetOccurrenceForUpdateAsync(Guid ledgerId, Guid occurrenceId, CancellationToken cancellationToken)
    {
        return _dbContext.PlanOccurrences
            .SingleOrDefaultAsync(occurrence => occurrence.Id == occurrenceId && occurrence.LedgerId == ledgerId, cancellationToken);
    }

    public Task<PlanOccurrence?> FindOccurrenceAsync(Guid planId, string periodKey, CancellationToken cancellationToken)
    {
        return _dbContext.PlanOccurrences
            .AsNoTracking()
            .SingleOrDefaultAsync(
                occurrence => occurrence.PlanId == planId && occurrence.PeriodKey == periodKey,
                cancellationToken);
    }

    public async Task AddOccurrenceAsync(PlanOccurrence occurrence, CancellationToken cancellationToken)
    {
        await _dbContext.PlanOccurrences.AddAsync(occurrence, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
