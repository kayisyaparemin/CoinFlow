using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class FinancialProjectionTests
{
    private readonly FinancialProjectionCalculator _calculator =
        TestFactory.ProjectionCalculator();

    [Fact]
    public void FirstFourCanonicalSalaryPeriods_AreExact()
    {
        var rows = _calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            4);

        AssertRow(
            rows[0], 115_000m, 21_875.82m, 23_156.56m, 28_167.40m,
            73_199.78m, 41_800.22m, 30_000m, 11_800.22m);
        AssertRow(
            rows[1], 115_000m, 21_875.82m, 20_109.28m, 28_167.40m,
            70_152.50m, 44_847.50m, 30_000m, 14_847.50m);
        AssertRow(
            rows[2], 115_000m, 21_875.82m, 15_706.73m, 55_492.20m,
            93_074.75m, 21_925.25m, 30_000m, -8_074.75m);

        Assert.Equal(11_800.22m, rows[0].EndingProjectedSavings);
        Assert.Equal(26_647.72m, rows[1].EndingProjectedSavings);
        Assert.Equal(18_572.97m, rows[2].EndingProjectedSavings);
        Assert.Equal(new DateOnly(2026, 12, 10), rows[3].PeriodStart);
    }

    [Fact]
    public void CanonicalUpcomingPlan_ExposesPreSalaryObligationsSeparately()
    {
        var result = _calculator.CalculatePlan(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            12);

        Assert.Equal(2, result.FundingPlan.PreFirstSalaryObligations.Count);
        Assert.Equal(
            53_095.50m,
            result.FundingPlan.PreFirstSalaryObligations.Sum(x => x.Amount));
        Assert.Equal(
            result.FundingPlan.EligiblePaymentCount,
            result.FundingPlan.AssignedExactlyOnceCount);
        Assert.Equal(0, result.FundingPlan.UnassignedPaymentCount);
        Assert.Equal(0, result.FundingPlan.DuplicateAssignedCount);
    }

    [Fact]
    public void AvailableAfterMandatory_IsNotClamped()
    {
        var plan = BasicPlan(100m) with
        {
            Loans =
            [
                new Loan
                {
                    MonthlyPayment = 500m,
                    PaymentDay = 18,
                    NextPaymentDate = new DateOnly(2026, 9, 18),
                    RemainingInstallmentCount = 1
                }
            ]
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(-400m, row.AvailableAfterMandatory);
    }

    [Fact]
    public void LivingBudget_ProducesNegativeSavingsCapacity()
    {
        var plan = BasicPlan(25_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m
            }
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(25_000m, row.AvailableAfterMandatory);
        Assert.Equal(-5_000m, row.EstimatedSavingsCapacity);
        Assert.True(row.HasDeficit);
    }

    [Fact]
    public void ProjectedSavings_IsCumulativeFromConfiguredStart()
    {
        var plan = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = 100_000m
            }
        };

        var rows = _calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            3);

        Assert.Equal(100_000m, rows[0].OpeningProjectedSavings);
        Assert.Equal(120_000m, rows[0].EndingProjectedSavings);
        Assert.Equal(120_000m, rows[1].OpeningProjectedSavings);
        Assert.Equal(160_000m, rows[2].EndingProjectedSavings);
    }

    [Fact]
    public void LargeCashExpense_ImpactsItsPeriodAndAllFutureSavings()
    {
        var baseline = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m
            }
        };
        var scenario = baseline with
        {
            PlannedLargeExpenses =
            [
                new PlannedLargeExpense
                {
                    Name = "Tadilat",
                    Amount = 350_000m,
                    ExactDate = new DateOnly(2026, 9, 15)
                }
            ]
        };

        var baselineRows = _calculator.Calculate(
            baseline, new DateOnly(2026, 8, 20), 3);
        var scenarioRows = _calculator.Calculate(
            scenario, new DateOnly(2026, 8, 20), 3);

        Assert.Equal(350_000m, scenarioRows[0].PlannedLargeCashExpenses);
        Assert.Equal(
            baselineRows[0].EndingProjectedSavings - 350_000m,
            scenarioRows[0].EndingProjectedSavings);
        Assert.Equal(
            baselineRows[2].EndingProjectedSavings - 350_000m,
            scenarioRows[2].EndingProjectedSavings);
    }

    [Fact]
    public void JanuarySalaryIncrease_StartsWithJanuarySalaryPeriod()
    {
        var rows = _calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 12, 10),
            5);

        var december = rows.Single(x =>
            x.PeriodStart == new DateOnly(2026, 12, 10));
        var january = rows.Single(x =>
            x.PeriodStart == new DateOnly(2027, 1, 10));
        Assert.Equal(115_000m, december.SalaryIncome);
        Assert.Equal(132_250m, january.SalaryIncome);
    }

    [Fact]
    public void OtherIncome_IsAddedOnlyToItsExactSalaryPeriod()
    {
        var plan = BasicPlan(100_000m) with
        {
            OtherIncomes =
            [
                new OneTimeIncome
                {
                    Amount = 50_000m,
                    ExactDate = new DateOnly(2026, 9, 15)
                }
            ]
        };

        var rows = _calculator.Calculate(
            plan, new DateOnly(2026, 8, 20), 3);

        Assert.Equal(50_000m, rows[0].OtherIncome);
        Assert.Equal(0m, rows[1].OtherIncome);
        Assert.Equal(0m, rows[2].OtherIncome);
    }

    [Fact]
    public void CardFallback_IsIncludedAndMarkedEstimated()
    {
        var row = Assert.Single(_calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(23_156.56m, row.CreditCardPayments);
        Assert.True(row.IsEstimatedCardPayment);
        Assert.False(row.HasUndeterminedCardPayment);
    }

    [Fact]
    public void AskEachWithoutFallback_IsVisibleAsUndetermined()
    {
        var plan = BasicPlan(100_000m) with
        {
            CreditCards =
            [
                TestFactory.AxessCard() with
                {
                    ProjectionFallbackStrategy =
                        ProjectionFallbackStrategy.None
                }
            ]
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(0m, row.CreditCardPayments);
        Assert.True(row.HasUndeterminedCardPayment);
    }

    private static FinancialPlan BasicPlan(decimal salary) => new()
    {
        Settings = new UserSettings
        {
            SalaryDay = 10,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20)
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Amount = salary,
                EffectiveDate = new DateOnly(2026, 1, 1)
            }
        ],
        PaymentAssignmentStrategies =
        [
            new PaymentAssignmentStrategy
            {
                Mode = PaymentAssignmentMode.UpcomingPeriod,
                EffectiveFromSalaryDate = new DateOnly(2026, 9, 10)
            }
        ]
    };

    private static void AssertRow(
        SalaryPeriodProjection row,
        decimal income,
        decimal loans,
        decimal card,
        decimal temporary,
        decimal mandatory,
        decimal available,
        decimal living,
        decimal savings)
    {
        Assert.Equal(income, row.TotalIncome);
        Assert.Equal(loans, row.LoanPayments);
        Assert.Equal(card, row.CreditCardPayments);
        Assert.Equal(temporary, row.TemporaryPayments);
        Assert.Equal(mandatory, row.MandatoryOutflow);
        Assert.Equal(available, row.AvailableAfterMandatory);
        Assert.Equal(living, row.LivingBudget);
        Assert.Equal(savings, row.EstimatedSavingsCapacity);
    }
}
