using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class PaymentAssignmentTests
{
    private readonly PaymentAssignmentStrategyResolver _resolver = new(
        new SalaryPeriodCalculator());

    [Fact]
    public void StrategyResolver_UsesNewestEffectiveRecord()
    {
        var history = History(
            (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2026, 12, 10), PaymentAssignmentMode.UpcomingPeriod));

        Assert.Equal(
            PaymentAssignmentMode.PreviousPeriod,
            _resolver.Resolve(new DateOnly(2026, 11, 10), history).Mode);
        Assert.Equal(
            PaymentAssignmentMode.UpcomingPeriod,
            _resolver.Resolve(new DateOnly(2026, 12, 10), history).Mode);
    }

    [Fact]
    public void StrategyResolver_InfersThreeHistoryRangesWithoutMutatingRecords()
    {
        var history = History(
            (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2026, 12, 10), PaymentAssignmentMode.UpcomingPeriod),
            (new DateOnly(2027, 4, 10), PaymentAssignmentMode.PreviousPeriod));
        var original = history.Select(x =>
            (x.Id, x.EffectiveFromSalaryDate, x.Mode)).ToArray();

        var expected = new[]
        {
            (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2026, 10, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2026, 11, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2026, 12, 10), PaymentAssignmentMode.UpcomingPeriod),
            (new DateOnly(2027, 1, 10), PaymentAssignmentMode.UpcomingPeriod),
            (new DateOnly(2027, 2, 10), PaymentAssignmentMode.UpcomingPeriod),
            (new DateOnly(2027, 3, 10), PaymentAssignmentMode.UpcomingPeriod),
            (new DateOnly(2027, 4, 10), PaymentAssignmentMode.PreviousPeriod),
            (new DateOnly(2027, 5, 10), PaymentAssignmentMode.PreviousPeriod)
        };

        foreach (var (salaryDate, mode) in expected)
        {
            Assert.Equal(mode, _resolver.Resolve(salaryDate, history).Mode);
        }

        Assert.Equal(original, history.Select(x =>
            (x.Id, x.EffectiveFromSalaryDate, x.Mode)).ToArray());
    }

    [Fact]
    public void StrategyResolver_RejectsMidPeriodEffectiveDate()
    {
        var history = History(
            (new DateOnly(2026, 9, 15), PaymentAssignmentMode.PreviousPeriod));

        Assert.Throws<InvalidOperationException>(() =>
            _resolver.ValidateHistory(
                history,
                10,
                new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public void UpcomingInitialPeriod_SeparatesPreSalaryObligations()
    {
        var plan = Plan(
            PaymentAssignmentMode.UpcomingPeriod,
            Dates(25, 5, 10, 18));

        Assert.Collection(
            plan.PreFirstSalaryObligations,
            item => Assert.Equal(new DateOnly(2026, 8, 25), item.DueDate),
            item => Assert.Equal(new DateOnly(2026, 9, 5), item.DueDate));
        Assert.Equal(
            new[] { new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 18) },
            plan.Budgets[0].Items.Select(x => x.DueDate));
    }

    [Fact]
    public void PreviousInitialPeriod_StartsAtSnapshotAnchor()
    {
        var obligations = Dates(19, 25, 5, 10, 18);
        var plan = Plan(PaymentAssignmentMode.PreviousPeriod, obligations);

        Assert.Empty(plan.PreFirstSalaryObligations);
        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 25),
                new DateOnly(2026, 9, 5),
                new DateOnly(2026, 9, 10)
            },
            plan.Budgets[0].Items.Select(x => x.DueDate));
        Assert.Equal(
            new DateOnly(2026, 10, 10),
            plan.Budgets[1].Items.Single(x =>
                x.DueDate == new DateOnly(2026, 9, 18)).AssignedSalaryDate);
    }

    [Fact]
    public void SteadyPrevious_AssignsOpenClosedSalaryWindow()
    {
        var obligations = Items(
            new DateOnly(2026, 9, 18),
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 11, 5),
            new DateOnly(2026, 11, 10));
        var plan = Plan(PaymentAssignmentMode.PreviousPeriod, obligations);

        Assert.All(
            plan.Budgets[1].Items,
            x => Assert.Equal(new DateOnly(2026, 10, 10), x.AssignedSalaryDate));
        Assert.Equal(3, plan.Budgets[1].Items.Count);
        Assert.Equal(3, plan.Budgets[2].Items.Count);
    }

    [Fact]
    public void SteadyUpcoming_AssignsClosedOpenSalaryWindow()
    {
        var obligations = Items(
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 18),
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 10, 9),
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 11, 5));
        var plan = Plan(PaymentAssignmentMode.UpcomingPeriod, obligations);

        Assert.Equal(4, plan.Budgets[0].Items.Count);
        Assert.Equal(3, plan.Budgets[1].Items.Count);
        Assert.Equal(0, plan.UnassignedPaymentCount);
        Assert.Equal(0, plan.DuplicateAssignedCount);
    }

    [Fact]
    public void PreviousToUpcoming_CatchesUpGapAndFundsForwardHorizon()
    {
        var periods = Periods(6);
        var obligations = Items(
            new DateOnly(2026, 11, 15),
            new DateOnly(2026, 12, 5),
            new DateOnly(2026, 12, 10),
            new DateOnly(2026, 12, 20),
            new DateOnly(2027, 1, 5));
        var planner = new SalaryFundingPlanner(_resolver);
        var result = planner.Plan(
            periods,
            obligations,
            new DateOnly(2026, 8, 20),
            10,
            History(
                (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
                (new DateOnly(2026, 12, 10), PaymentAssignmentMode.UpcomingPeriod)));
        var transition = result.Budgets.Single(x =>
            x.SalaryDate == new DateOnly(2026, 12, 10));

        Assert.Equal(5, transition.Items.Count);
        Assert.All(transition.Items, item => Assert.Equal(
            new DateOnly(2026, 12, 10), item.AssignedSalaryDate));
        Assert.All(
            transition.Items.Where(x => x.DueDate < transition.SalaryDate),
            item => Assert.Equal(
                PaymentAssignmentReason.TransitionCatchUp,
                item.AssignmentReason));
        Assert.Equal(2_000m, transition.TransitionCatchUpAmount);
        Assert.Equal(3_000m, transition.ForwardFundedAmount);
        AssertInvariant(result);
    }

    [Fact]
    public void FutureStrategyRecord_DoesNotRewriteEarlierSalaryAssignments()
    {
        var obligations = Items(
            new DateOnly(2026, 9, 18),
            new DateOnly(2026, 10, 5),
            new DateOnly(2026, 10, 18),
            new DateOnly(2026, 11, 5));
        var planner = new SalaryFundingPlanner(_resolver);
        var previousOnly = planner.Plan(
            Periods(5),
            obligations,
            new DateOnly(2026, 8, 20),
            10,
            History((new DateOnly(2026, 9, 10),
                PaymentAssignmentMode.PreviousPeriod)));
        var withFutureChange = planner.Plan(
            Periods(5),
            obligations,
            new DateOnly(2026, 8, 20),
            10,
            History(
                (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
                (new DateOnly(2026, 12, 10), PaymentAssignmentMode.UpcomingPeriod)));

        Assert.Equal(
            previousOnly.Budgets.Take(3)
                .SelectMany(x => x.Items)
                .Select(x => (x.DueDate, x.AssignedSalaryDate)),
            withFutureChange.Budgets.Take(3)
                .SelectMany(x => x.Items)
                .Select(x => (x.DueDate, x.AssignedSalaryDate)));
    }

    [Fact]
    public void UpcomingToPrevious_DoesNotAssignFundedDatesTwice()
    {
        var result = new SalaryFundingPlanner(_resolver).Plan(
            Periods(6),
            Items(
                new DateOnly(2026, 11, 15),
                new DateOnly(2026, 12, 5),
                new DateOnly(2026, 12, 10),
                new DateOnly(2026, 12, 15)),
            new DateOnly(2026, 8, 20),
            10,
            History(
                (new DateOnly(2026, 9, 10), PaymentAssignmentMode.UpcomingPeriod),
                (new DateOnly(2026, 12, 10), PaymentAssignmentMode.PreviousPeriod)));
        var november = result.Budgets.Single(x =>
            x.SalaryDate == new DateOnly(2026, 11, 10));
        var december = result.Budgets.Single(x =>
            x.SalaryDate == new DateOnly(2026, 12, 10));
        var january = result.Budgets.Single(x =>
            x.SalaryDate == new DateOnly(2027, 1, 10));

        Assert.Equal(2, november.Items.Count);
        Assert.Equal(new DateOnly(2026, 12, 10), december.Items.Single().DueDate);
        Assert.Equal(new DateOnly(2026, 12, 15), january.Items.Single().DueDate);
        AssertInvariant(result);
    }

    [Fact]
    public void MultipleTransitions_AssignEveryEligiblePaymentExactlyOnce()
    {
        var obligations = Enumerable.Range(0, 170)
            .Select(index => new ObligationItem(
                $"Ödeme {index}",
                ObligationType.OtherScheduledPayment,
                new DateOnly(2026, 8, 20).AddDays(index),
                1m,
                PaymentId: Guid.NewGuid()))
            .ToArray();
        var result = new SalaryFundingPlanner(_resolver).Plan(
            Periods(7),
            obligations,
            new DateOnly(2026, 8, 20),
            10,
            History(
                (new DateOnly(2026, 9, 10), PaymentAssignmentMode.PreviousPeriod),
                (new DateOnly(2026, 11, 10), PaymentAssignmentMode.UpcomingPeriod),
                (new DateOnly(2027, 1, 10), PaymentAssignmentMode.PreviousPeriod)));

        AssertInvariant(result);
    }

    [Fact]
    public void ProjectionAnchor_StartsAtFirstSalaryOnOrAfterAnchor()
    {
        var periods = TestFactory.ProjectionCalculator().Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            12);

        Assert.Equal(new DateOnly(2026, 9, 10), periods[0].PeriodStart);
        Assert.DoesNotContain(
            periods,
            x => x.PeriodStart == new DateOnly(2026, 8, 10));
    }

    private SalaryFundingPlan Plan(
        PaymentAssignmentMode mode,
        IReadOnlyList<ObligationItem> obligations) =>
        new SalaryFundingPlanner(_resolver).Plan(
            Periods(4),
            obligations,
            new DateOnly(2026, 8, 20),
            10,
            History((new DateOnly(2026, 9, 10), mode)));

    private static IReadOnlyList<SalaryPeriod> Periods(int count) =>
        new SalaryPeriodCalculator().GetPeriods(
            new DateOnly(2026, 9, 10),
            10,
            count);

    private static IReadOnlyList<PaymentAssignmentStrategy> History(
        params (DateOnly Date, PaymentAssignmentMode Mode)[] values) => values
        .Select(value => new PaymentAssignmentStrategy
        {
            Mode = value.Mode,
            EffectiveFromSalaryDate = value.Date
        })
        .ToArray();

    private static IReadOnlyList<ObligationItem> Dates(
        int augustPast,
        int augustFuture,
        int septemberBefore,
        int septemberSalary,
        int septemberAfter) => Items(
            new DateOnly(2026, 8, augustPast),
            new DateOnly(2026, 8, augustFuture),
            new DateOnly(2026, 9, septemberBefore),
            new DateOnly(2026, 9, septemberSalary),
            new DateOnly(2026, 9, septemberAfter));

    private static IReadOnlyList<ObligationItem> Dates(
        int augustFuture,
        int septemberBefore,
        int septemberSalary,
        int septemberAfter) => Items(
            new DateOnly(2026, 8, augustFuture),
            new DateOnly(2026, 9, septemberBefore),
            new DateOnly(2026, 9, septemberSalary),
            new DateOnly(2026, 9, septemberAfter));

    private static IReadOnlyList<ObligationItem> Items(
        params DateOnly[] dates) => dates
        .Select((date, index) => new ObligationItem(
            $"Ödeme {index}",
            ObligationType.OtherScheduledPayment,
            date,
            1_000m,
            PaymentId: Guid.NewGuid()))
        .ToArray();

    private static void AssertInvariant(SalaryFundingPlan result)
    {
        Assert.Equal(result.EligiblePaymentCount, result.AssignedExactlyOnceCount);
        Assert.Equal(0, result.UnassignedPaymentCount);
        Assert.Equal(0, result.DuplicateAssignedCount);
    }
}
