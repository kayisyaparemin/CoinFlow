using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class FinancialProjectionService(
    FinancialProjectionCalculator projectionCalculator)
{
    public DashboardSnapshot BuildDashboard(
        FinancialPlan plan,
        DateOnly asOf,
        DateOnly? firstSalaryDate = null)
    {
        var projection = projectionCalculator.CalculatePlan(
            plan,
            asOf,
            12,
            firstSalaryDate);
        var periods = projection.Periods;
        var current = periods[0];
        var preFirst = projection.FundingPlan.PreFirstSalaryObligations
            .Where(x => x.DueDate >= asOf)
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.Amount)
            .ToArray();
        var upcoming = preFirst
            .Concat(periods
            .SelectMany(x => x.MandatoryItems)
            .Where(x => x.DueDate >= asOf)
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.Amount))
            .Take(5)
            .ToArray();
        var tightest = periods
            .OrderBy(x => x.EstimatedSavingsCapacity)
            .ThenBy(x => x.PeriodStart)
            .First();

        var orderedStrategies = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .ToArray();
        var currentStrategy = orderedStrategies
            .Where(x => x.EffectiveFromSalaryDate <= current.PeriodStart)
            .Last();
        var pending = orderedStrategies.FirstOrDefault(x =>
            x.EffectiveFromSalaryDate > current.PeriodStart);

        return new DashboardSnapshot(
            current,
            preFirst,
            upcoming,
            periods[^1].EndingProjectedSavings,
            tightest,
            periods.Any(x => x.HasUndeterminedCardPayment),
            currentStrategy,
            pending,
            plan.Settings.ProjectionAnchorDate,
            projection.TotalCreditCardInterest,
            projection.TotalDeficitFinancingInterest,
            projection.TotalInterestCost);
    }

    public IReadOnlyList<SalaryPeriodProjection> BuildFuturePeriods(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount,
        DateOnly? firstSalaryDate = null) =>
        projectionCalculator.Calculate(plan, asOf, periodCount, firstSalaryDate);
}
