using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class FinancialProjectionService(
    FinancialProjectionCalculator projectionCalculator)
{
    public DashboardSnapshot BuildDashboard(
        FinancialPlan plan,
        DateOnly asOf)
    {
        var periods = projectionCalculator.Calculate(plan, asOf, 12);
        var current = periods[0];
        var upcoming = periods
            .SelectMany(x => x.MandatoryItems)
            .Where(x => x.DueDate >= asOf)
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.Amount)
            .Take(5)
            .ToArray();
        var tightest = periods
            .OrderBy(x => x.EstimatedSavingsCapacity)
            .ThenBy(x => x.PeriodStart)
            .First();

        return new DashboardSnapshot(
            current,
            upcoming,
            periods[^1].EndingProjectedSavings,
            tightest,
            periods.Any(x => x.HasUndeterminedCardPayment));
    }

    public IReadOnlyList<SalaryPeriodProjection> BuildFuturePeriods(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount) =>
        projectionCalculator.Calculate(plan, asOf, periodCount);
}
