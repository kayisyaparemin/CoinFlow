using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record SalaryFundingBudget(
    DateOnly SalaryDate,
    DateOnly CoverageStart,
    DateOnly CoverageEnd,
    PaymentAssignmentMode Mode,
    bool IsStrategyTransition,
    bool IsInitialSnapshotPeriod,
    IReadOnlyList<ObligationItem> Items)
{
    private IEnumerable<ObligationItem> MandatoryItems =>
        Items.Where(x => x.Type != ObligationType.PlannedLargeExpense);

    public decimal TransitionCatchUpAmount => MandatoryItems
        .Where(x => x.IsTransitionCatchUp)
        .Sum(x => x.Amount);

    public decimal ForwardFundedAmount => MandatoryItems
        .Where(x => x.IsForwardFunded)
        .Sum(x => x.Amount);

    public decimal NormalMandatoryAmount => MandatoryItems
        .Where(x => x.AssignmentReason is not
            PaymentAssignmentReason.TransitionCatchUp and not
            PaymentAssignmentReason.TransitionForward)
        .Sum(x => x.Amount);

    public PaymentAssignmentReason ResolveReason(DateOnly paymentDate) =>
        Items.FirstOrDefault(x => x.DueDate == paymentDate)
            ?.AssignmentReason ??
        (Mode == PaymentAssignmentMode.PreviousPeriod
            ? PaymentAssignmentReason.NormalPrevious
            : PaymentAssignmentReason.NormalUpcoming);
}

public sealed record SalaryFundingPlan(
    IReadOnlyList<SalaryFundingBudget> Budgets,
    IReadOnlyList<ObligationItem> PreFirstSalaryObligations,
    int EligiblePaymentCount,
    int AssignedExactlyOnceCount,
    int UnassignedPaymentCount,
    int DuplicateAssignedCount)
{
    public SalaryFundingBudget? FindBudget(DateOnly paymentDate) =>
        Budgets.FirstOrDefault(x =>
            paymentDate >= x.CoverageStart &&
            paymentDate <= x.CoverageEnd);

    public bool IsPreFirstSalaryDate(DateOnly paymentDate) =>
        PreFirstSalaryObligations.Any(x => x.DueDate == paymentDate);
}

public sealed class SalaryFundingPlanner(
    PaymentAssignmentStrategyResolver strategyResolver)
{
    public SalaryFundingPlan Plan(
        IReadOnlyList<SalaryPeriod> salaryPeriods,
        IEnumerable<ObligationItem> obligations,
        DateOnly projectionAnchorDate,
        int salaryDay,
        IReadOnlyList<PaymentAssignmentStrategy> strategyHistory)
    {
        if (salaryPeriods.Count == 0)
        {
            throw new ArgumentException(
                "En az bir projection maaş dönemi gereklidir.",
                nameof(salaryPeriods));
        }

        var firstSalary = salaryPeriods[0].Start;
        strategyResolver.ValidateHistory(
            strategyHistory,
            salaryDay,
            firstSalary);
        var indexed = obligations
            .Where(x => x.DueDate >= projectionAnchorDate)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Name)
            .Select((item, index) => new IndexedObligation(index, item))
            .ToArray();
        var assignmentCounts = indexed.ToDictionary(x => x.Index, _ => 0);
        var firstMode = strategyResolver
            .Resolve(firstSalary, strategyHistory)
            .Mode;
        var preFirst = new List<ObligationItem>();

        if (firstMode == PaymentAssignmentMode.UpcomingPeriod)
        {
            foreach (var entry in indexed.Where(x =>
                         x.Item.DueDate < firstSalary))
            {
                assignmentCounts[entry.Index]++;
                preFirst.Add(entry.Item with
                {
                    ActiveMode = firstMode,
                    AssignmentReason =
                        PaymentAssignmentReason.PreFirstSalaryUpcoming,
                    IsPreFirstSalaryObligation = true
                });
            }
        }

        var lastCoveredDate = firstMode ==
                              PaymentAssignmentMode.UpcomingPeriod
            ? firstSalary.AddDays(-1)
            : projectionAnchorDate.AddDays(-1);
        var budgets = new List<SalaryFundingBudget>(salaryPeriods.Count);
        PaymentAssignmentMode? previousMode = null;

        foreach (var period in salaryPeriods)
        {
            var strategy = strategyResolver.Resolve(
                period.Start,
                strategyHistory);
            var mode = strategy.Mode;
            var isTransition = previousMode is not null &&
                               previousMode != mode;
            var coverageStart = lastCoveredDate.AddDays(1);
            var targetCoveredDate = mode ==
                                    PaymentAssignmentMode.PreviousPeriod
                ? period.Start
                : period.End.AddDays(-1);
            var assigned = new List<ObligationItem>();

            if (coverageStart <= targetCoveredDate)
            {
                foreach (var entry in indexed.Where(x =>
                             x.Item.DueDate >= coverageStart &&
                             x.Item.DueDate <= targetCoveredDate))
                {
                    assignmentCounts[entry.Index]++;
                    var reason = ResolveReason(
                        salaryPeriods[0].Start == period.Start,
                        isTransition,
                        previousMode,
                        mode,
                        entry.Item.DueDate,
                        period.Start);
                    assigned.Add(entry.Item with
                    {
                        AssignedSalaryDate = period.Start,
                        PaymentBeforeSalary =
                            entry.Item.DueDate < period.Start,
                        ActiveMode = mode,
                        AssignmentReason = reason,
                        IsTransitionCatchUp = reason ==
                            PaymentAssignmentReason.TransitionCatchUp,
                        IsForwardFunded =
                            mode == PaymentAssignmentMode.UpcomingPeriod &&
                            entry.Item.DueDate >= period.Start
                    });
                }
            }

            budgets.Add(new SalaryFundingBudget(
                period.Start,
                coverageStart,
                targetCoveredDate,
                mode,
                isTransition,
                salaryPeriods[0].Start == period.Start &&
                mode == PaymentAssignmentMode.PreviousPeriod,
                assigned));
            if (targetCoveredDate > lastCoveredDate)
            {
                lastCoveredDate = targetCoveredDate;
            }

            previousMode = mode;
        }

        var eligibleIndexes = indexed
            .Where(x => x.Item.DueDate <= lastCoveredDate)
            .Select(x => x.Index)
            .ToArray();
        var exactlyOnce = eligibleIndexes.Count(x =>
            assignmentCounts[x] == 1);
        var unassigned = eligibleIndexes.Count(x =>
            assignmentCounts[x] == 0);
        var duplicate = eligibleIndexes.Count(x =>
            assignmentCounts[x] > 1);

        return new SalaryFundingPlan(
            budgets,
            preFirst,
            eligibleIndexes.Length,
            exactlyOnce,
            unassigned,
            duplicate);
    }

    private static PaymentAssignmentReason ResolveReason(
        bool isFirstSalary,
        bool isTransition,
        PaymentAssignmentMode? previousMode,
        PaymentAssignmentMode mode,
        DateOnly paymentDate,
        DateOnly salaryDate)
    {
        if (isFirstSalary && mode == PaymentAssignmentMode.PreviousPeriod)
        {
            return PaymentAssignmentReason.InitialSnapshotCatchUp;
        }

        if (isTransition &&
            previousMode == PaymentAssignmentMode.PreviousPeriod &&
            mode == PaymentAssignmentMode.UpcomingPeriod)
        {
            return paymentDate < salaryDate
                ? PaymentAssignmentReason.TransitionCatchUp
                : PaymentAssignmentReason.TransitionForward;
        }

        return mode == PaymentAssignmentMode.PreviousPeriod
            ? PaymentAssignmentReason.NormalPrevious
            : PaymentAssignmentReason.NormalUpcoming;
    }

    private sealed record IndexedObligation(
        int Index,
        ObligationItem Item);
}
