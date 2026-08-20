using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

public sealed class PaymentAssignmentTests
{
    private readonly PaymentAssignmentResolver _resolver = new(
        new SalaryPeriodCalculator());
    private readonly FinancialProjectionCalculator _projection =
        TestFactory.ProjectionCalculator();

    [Theory]
    [InlineData("2026-09-05", "2026-08-10")]
    [InlineData("2026-09-07", "2026-08-10")]
    [InlineData("2026-09-10", "2026-09-10")]
    [InlineData("2026-09-18", "2026-09-10")]
    [InlineData("2026-10-05", "2026-09-10")]
    [InlineData("2026-10-07", "2026-09-10")]
    [InlineData("2026-10-10", "2026-10-10")]
    [InlineData("2026-10-18", "2026-10-10")]
    public void UpcomingPeriod_MapsExactPaymentToContainingSalaryBudget(
        string paymentText,
        string expectedSalaryText)
    {
        var salaryDate = _resolver.ResolveFundingSalaryDate(
            DateOnly.Parse(paymentText),
            10,
            PaymentAssignmentMode.UpcomingPeriod);

        Assert.Equal(DateOnly.Parse(expectedSalaryText), salaryDate);
    }

    [Theory]
    [InlineData("2026-09-10", "2026-09-10")]
    [InlineData("2026-09-11", "2026-10-10")]
    [InlineData("2026-09-18", "2026-10-10")]
    [InlineData("2026-10-05", "2026-10-10")]
    [InlineData("2026-10-09", "2026-10-10")]
    [InlineData("2026-10-10", "2026-10-10")]
    [InlineData("2026-10-18", "2026-11-10")]
    public void PreviousPeriod_UsesOpenStartAndClosedSalaryDayBoundary(
        string paymentText,
        string expectedSalaryText)
    {
        var salaryDate = _resolver.ResolveFundingSalaryDate(
            DateOnly.Parse(paymentText),
            10,
            PaymentAssignmentMode.PreviousPeriod);

        Assert.Equal(DateOnly.Parse(expectedSalaryText), salaryDate);
    }

    [Fact]
    public void AssignmentWindows_RespectConfiguredSalaryDay()
    {
        var salaryDate = new DateOnly(2026, 10, 15);
        var previous = _resolver.ResolveWindow(
            salaryDate,
            15,
            PaymentAssignmentMode.PreviousPeriod);
        var upcoming = _resolver.ResolveWindow(
            salaryDate,
            15,
            PaymentAssignmentMode.UpcomingPeriod);

        Assert.Equal(new DateOnly(2026, 9, 16), previous.StartInclusive);
        Assert.Equal(new DateOnly(2026, 10, 15), previous.EndInclusive);
        Assert.Equal(new DateOnly(2026, 10, 15), upcoming.StartInclusive);
        Assert.Equal(new DateOnly(2026, 11, 14), upcoming.EndInclusive);
    }

    [Fact]
    public void PreviousPeriod_MonthEndSalaryBoundaryIsNotShiftedBack()
    {
        Assert.Equal(
            new DateOnly(2027, 2, 28),
            _resolver.ResolveFundingSalaryDate(
                new DateOnly(2027, 2, 28),
                31,
                PaymentAssignmentMode.PreviousPeriod));
        Assert.Equal(
            new DateOnly(2027, 2, 28),
            _resolver.ResolveFundingSalaryDate(
                new DateOnly(2027, 2, 27),
                31,
                PaymentAssignmentMode.PreviousPeriod));
    }

    [Fact]
    public void PreviousPeriod_ShiftsCanonicalPaymentsWithoutChangingDates()
    {
        var plan = TestFactory.CanonicalPlan() with
        {
            Settings = TestFactory.CanonicalPlan().Settings with
            {
                PaymentAssignmentMode = PaymentAssignmentMode.PreviousPeriod
            }
        };
        var rows = _projection.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            4);
        var items = rows.SelectMany(x => x.MandatoryItems).ToArray();

        AssertAssignment(items, ObligationType.CreditCard,
            new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 10));
        AssertAssignment(items, ObligationType.Loan,
            new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 10));
        AssertAssignment(items, ObligationType.Loan,
            new DateOnly(2026, 9, 18), new DateOnly(2026, 10, 10));
        AssertAssignment(items, ObligationType.TemporaryPayment,
            new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 10));
        AssertAssignment(items, ObligationType.CreditCard,
            new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 10));
        AssertAssignment(items, ObligationType.Loan,
            new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 10));
        AssertAssignment(items, ObligationType.Loan,
            new DateOnly(2026, 10, 18), new DateOnly(2026, 11, 10));
        AssertAssignment(items, ObligationType.TemporaryPayment,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 10));
    }

    [Fact]
    public void PreviousPeriod_FirstFourCanonicalBudgetsAreShiftedExactly()
    {
        var canonical = TestFactory.CanonicalPlan();
        var plan = canonical with
        {
            Settings = canonical.Settings with
            {
                PaymentAssignmentMode = PaymentAssignmentMode.PreviousPeriod
            }
        };

        var rows = _projection.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            4);

        Assert.Equal(
            [0m, 53_095.50m, 73_199.78m, 70_152.50m],
            rows.Select(x => x.MandatoryOutflow).ToArray());
        Assert.Equal(
            [85_000m, 31_904.50m, 11_800.22m, 14_847.50m],
            rows.Select(x => x.EstimatedSavingsCapacity).ToArray());
        Assert.Equal(
            [85_000m, 116_904.50m, 128_704.72m, 143_552.22m],
            rows.Select(x => x.EndingProjectedSavings).ToArray());
    }

    [Fact]
    public void TargetReachDate_RecalculatesWhenAssignmentModeChanges()
    {
        var canonical = TestFactory.CanonicalPlan();
        var upcoming = _projection.Calculate(
            canonical,
            new DateOnly(2026, 8, 20),
            4);
        var previous = _projection.Calculate(
            canonical with
            {
                Settings = canonical.Settings with
                {
                    PaymentAssignmentMode =
                        PaymentAssignmentMode.PreviousPeriod
                }
            },
            new DateOnly(2026, 8, 20),
            4);
        var target = new TargetAmountCalculator();

        Assert.Null(target.FindFirstReached(upcoming, 100_000m));
        Assert.Equal(
            new DateOnly(2026, 9, 10),
            target.FindFirstReached(previous, 100_000m)?.PeriodStart);
    }

    [Fact]
    public void CardAssignment_UsesDueDateAndPreservesStatementCloseDate()
    {
        var canonical = TestFactory.CanonicalPlan();
        var plan = canonical with
        {
            Settings = canonical.Settings with
            {
                PaymentAssignmentMode = PaymentAssignmentMode.PreviousPeriod
            }
        };

        var status = _projection.Calculate(
                plan,
                new DateOnly(2026, 8, 20),
                4)
            .SelectMany(x => x.CardPaymentStatuses)
            .Single(x => x.PaymentDueDate == new DateOnly(2026, 10, 5));

        Assert.Equal(new DateOnly(2026, 9, 25), status.StatementCloseDate);
        Assert.Equal(new DateOnly(2026, 10, 5), status.PaymentDueDate);
        Assert.Equal(new DateOnly(2026, 10, 10), status.AssignedSalaryDate);
        Assert.True(status.PaymentBeforeSalary);
    }

    [Fact]
    public void SalaryDayPayment_IsNeverFlaggedBeforeSalary()
    {
        var period = new SalaryPeriod(
            new DateOnly(2026, 10, 10),
            new DateOnly(2026, 11, 10));
        var calculator = new MandatoryPaymentCalculator(
            new LoanScheduleCalculator(),
            new ScheduledPaymentCalculator(),
            _resolver);

        var result = calculator.Calculate(
            period,
            [],
            [],
            [new ObligationItem(
                "Sınır ödemesi",
                ObligationType.CreditCard,
                new DateOnly(2026, 10, 10),
                1_000m)],
            10,
            PaymentAssignmentMode.PreviousPeriod);

        var item = Assert.Single(result.Items);
        Assert.Equal(period.Start, item.AssignedSalaryDate);
        Assert.False(item.PaymentBeforeSalary);
    }

    [Fact]
    public void Simulator_UsesSameModeForBaselineAndScenarioAndAllowsOverride()
    {
        var canonical = TestFactory.CanonicalPlan();
        var plan = canonical with
        {
            Settings = canonical.Settings with
            {
                PaymentAssignmentMode = PaymentAssignmentMode.PreviousPeriod
            }
        };
        var request = new SimulationRequest(
            SimulationScenarioType.CashDebt,
            "Test ödeme",
            9_000m,
            new DateOnly(2026, 9, 1),
            1,
            new DateOnly(2026, 9, 18));
        var calculator = new SimulationCalculator(
            _projection,
            new InstallmentScheduleCalculator());

        var previous = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            request,
            4);
        var upcomingOverride = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            request,
            4,
            PaymentAssignmentMode.UpcomingPeriod);

        Assert.All(previous.Baseline.Concat(previous.Scenario), row =>
            Assert.Equal(PaymentAssignmentMode.PreviousPeriod,
                row.PaymentAssignmentMode));
        Assert.Equal(-9_000m, previous.Rows.Single(x =>
            x.Scenario.PeriodStart == new DateOnly(2026, 10, 10))
            .AvailableDifference);
        Assert.All(upcomingOverride.Baseline.Concat(upcomingOverride.Scenario), row =>
            Assert.Equal(PaymentAssignmentMode.UpcomingPeriod,
                row.PaymentAssignmentMode));
        Assert.Equal(-9_000m, upcomingOverride.Rows.Single(x =>
            x.Scenario.PeriodStart == new DateOnly(2026, 9, 10))
            .AvailableDifference);
    }

    [Fact]
    public async Task DashboardAndTwelveMonth_RecalculateImmediatelyAfterModeSave()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-assignment-{Guid.NewGuid():N}.db");
        var store = new SqliteCoinFlowStore(
            path,
            true,
            new DateOnly(2026, 8, 20));
        try
        {
            var service = TestFactory.Service(store);
            var upcomingDashboard = await service.GetDashboardAsync();
            var settings = (await service.GetFinancialPlanAsync()).Settings;

            await service.SaveSettingsAsync(settings with
            {
                PaymentAssignmentMode =
                    PaymentAssignmentMode.PreviousPeriod
            });

            var previousDashboard = await service.GetDashboardAsync();
            var previousRows = await service.GetFuturePeriodsAsync(
                periodCount: 4);

            Assert.Equal(
                53_095.50m,
                upcomingDashboard.CurrentPeriod.MandatoryOutflow);
            Assert.Equal(
                0m,
                previousDashboard.CurrentPeriod.MandatoryOutflow);
            Assert.Equal(53_095.50m, previousRows[1].MandatoryOutflow);
        }
        finally
        {
            await store.DisposeAsync();
            foreach (var candidate in new[]
                     {
                         path,
                         path + "-shm",
                         path + "-wal"
                     })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    private static void AssertAssignment(
        IEnumerable<ObligationItem> items,
        ObligationType type,
        DateOnly exactDate,
        DateOnly assignedSalary)
    {
        var item = items.Single(x =>
            x.Type == type && x.DueDate == exactDate);
        Assert.Equal(exactDate, item.DueDate);
        Assert.Equal(assignedSalary, item.AssignedSalaryDate);
        Assert.Equal(exactDate < assignedSalary, item.PaymentBeforeSalary);
    }
}
