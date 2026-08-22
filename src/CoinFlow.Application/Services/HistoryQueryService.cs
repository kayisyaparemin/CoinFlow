using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;

namespace CoinFlow.Application.Services;

public sealed class HistoryQueryService(
    ICoinFlowStore store,
    PlanActualComparisonCalculator comparisonCalculator)
{
    public async Task<IReadOnlyList<HistoryPeriod>> GetPeriodsAsync(
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        return history.Actuals.Select(actual =>
            {
                var plan = history.Plans.Single(x =>
                    x.Id == actual.PeriodPlanSnapshotId);
                var revisions = history.Revisions
                    .Where(x => x.PeriodPlanSnapshotId == plan.Id)
                    .Where(x => DateOnly.FromDateTime(
                        x.CreatedAtUtc.UtcDateTime.Date) <=
                        plan.ReviewAvailableFrom)
                    .OrderBy(x => x.CreatedAtUtc)
                    .ThenBy(x => x.RevisionNumber)
                    .ToArray();
                var revision = revisions.LastOrDefault();
                var result = history.Snapshots.Single(x =>
                    x.Id == actual.ResultFinancialSnapshotId);
                return new HistoryPeriod(
                    plan,
                    revision,
                    revisions.Length,
                    actual,
                    result,
                    comparisonCalculator.Calculate(
                        plan,
                        revision,
                        actual));
            })
            .OrderByDescending(x => x.OriginalPlan.PeriodStart)
            .ToArray();
    }

    public async Task<HistoryPeriod> GetPeriodAsync(
        Guid actualId,
        CancellationToken cancellationToken = default) =>
        (await GetPeriodsAsync(cancellationToken)).Single(x =>
            x.Actual.Id == actualId);

    public async Task<HistorySummary?> GetRecentSummaryAsync(
        int periodCount = 3,
        CancellationToken cancellationToken = default)
    {
        var periods = (await GetPeriodsAsync(cancellationToken))
            .Take(periodCount)
            .ToArray();
        return periods.Length == 0
            ? null
            : new HistorySummary(
                periods.Sum(x => x.Comparison.PlannedEndingSavings),
                periods.Sum(x => x.Comparison.ActualEndingSavings),
                periods.Sum(x => x.Comparison.Difference),
                periods.Length);
    }
}
