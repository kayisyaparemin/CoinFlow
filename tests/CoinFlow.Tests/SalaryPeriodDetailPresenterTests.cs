using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class SalaryPeriodDetailPresenterTests
{
    private readonly SalaryPeriodDetailPresenter _presenter = new();
    private readonly FinancialProjectionCalculator _projection =
        TestFactory.ProjectionCalculator();

    [Fact]
    public void Build_MapsEngineValuesWithoutChangingProjectionResults()
    {
        var plan = TestFactory.CanonicalPlan();
        var before = _projection.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            12);
        var row = before[1];

        var detail = _presenter.Build(row);
        var after = _projection.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            12);

        Assert.Equal(row.TotalIncome, detail.IncomeSummary.Amount);
        Assert.Equal("115.000 TL", detail.IncomeSummary.AmountText);
        Assert.Equal(row.MandatoryOutflow, detail.MandatorySummary.Amount);
        Assert.Equal(
            row.EstimatedSavingsCapacity,
            detail.SavingsSummary.Amount);
        Assert.Equal(
            row.EndingProjectedSavings,
            detail.EndingSummary.Amount);
        Assert.Equal(
            before.Select(x => x.EndingProjectedSavings),
            after.Select(x => x.EndingProjectedSavings));
        Assert.Equal(
            before.Select(x => x.TotalInterestGenerated),
            after.Select(x => x.TotalInterestGenerated));
    }

    [Fact]
    public void Build_HidesZeroCategoriesAndCreatesIndependentPaymentRows()
    {
        var source = _projection.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            1)[0];
        var assignedSalary = source.PeriodStart;
        var row = source with
        {
            LoanPayments = 21_875.82m,
            CreditCardPayments = 0m,
            TemporaryPayments = 28_167.40m,
            InstallmentPayments = 0m,
            OtherScheduledPayments = 0m,
            MandatoryItems =
            [
                Payment("Burgan Bank On Dijital", ObligationType.Loan,
                    new DateOnly(2026, 9, 18), 7_374.59m,
                    assignedSalary, beforeSalary: true),
                Payment("Eminevim", ObligationType.TemporaryPayment,
                    new DateOnly(2026, 9, 20), 28_167.40m,
                    assignedSalary, beforeSalary: true),
                Payment("Garanti BBVA borç kapama", ObligationType.Loan,
                    new DateOnly(2026, 10, 7), 14_501.23m,
                    assignedSalary),
                Payment("Akbank Axess", ObligationType.CreditCard,
                    new DateOnly(2026, 10, 5), 23_156.56m,
                    assignedSalary, isEstimate: true)
            ],
            LargeExpenseItems = [],
            CardPaymentStatuses = []
        };

        var detail = _presenter.Build(row);

        Assert.Collection(
            detail.MandatoryRows,
            item => Assert.Equal("Krediler", item.Label),
            item => Assert.Equal("Geçici Ödeme Planları", item.Label));
        Assert.All(detail.MandatoryRows,
            item => Assert.NotEqual(0m, item.Amount));
        Assert.Equal(4, detail.PaymentRows.Count);
        Assert.Equal(4, detail.PaymentRows.Select(x => x.Name).Distinct().Count());
        Assert.Equal(2,
            detail.PaymentRows.Count(x => x.IsBeforeFundingSalary));
        Assert.Single(detail.PaymentRows.Where(x => x.IsEstimated));
        Assert.Equal(
            "28.167,40 TL",
            detail.PaymentRows.Single(x => x.Name == "Eminevim")
                .AmountText);
        Assert.False(detail.HasTransitionRows);
    }

    [Fact]
    public void Build_SimulatorModeProducesBaselineScenarioAndSemanticDeltas()
    {
        var result = new SimulationCalculator(
                _projection,
                new InstallmentScheduleCalculator())
            .Calculate(
                TestFactory.CanonicalPlan(),
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Tadilat",
                    350_000m,
                    new DateOnly(2027, 3, 15)));
        var impact = result.Rows.Single(x =>
            x.Scenario.Period.Contains(new DateOnly(2027, 3, 15)));

        var detail = _presenter.Build(
            impact.Scenario,
            impact.Baseline);

        Assert.True(detail.HasComparison);
        Assert.StartsWith("-", detail.EndingSummary.AmountText);
        Assert.Collection(
            detail.ComparisonRows,
            mandatory =>
            {
                Assert.Equal("Zorunlu", mandatory.Label);
                Assert.Equal(
                    impact.MandatoryOutflowDifference,
                    mandatory.Difference);
            },
            savings =>
            {
                Assert.Equal("Dönem neti", savings.Label);
                Assert.Equal(
                    impact.SavingsCapacityDifference,
                    savings.Difference);
                Assert.True(savings.IsUnfavorable);
            },
            interest =>
            {
                Assert.Equal("Faiz yükü", interest.Label);
                Assert.Equal(
                    impact.InterestDifference,
                    interest.Difference);
            },
            ending =>
            {
                Assert.Equal("Dönem sonu durumu", ending.Label);
                Assert.Equal(
                    impact.ProjectedSavingsDifference,
                    ending.Difference);
                Assert.True(ending.IsUnfavorable);
            });
    }

    [Fact]
    public void Build_ShowsInterestAndCarryDeficitOnlyWhenRelevant()
    {
        var result = new SimulationCalculator(
                _projection,
                new InstallmentScheduleCalculator())
            .Calculate(
                TestFactory.CanonicalPlan(),
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Tadilat",
                    350_000m,
                    new DateOnly(2027, 3, 15)));
        var negativeIndex = result.Scenario.ToList().FindIndex(x =>
            x.Period.Contains(new DateOnly(2027, 3, 15)));

        var negative = _presenter.Build(result.Scenario[negativeIndex]);
        var carry = _presenter.Build(result.Scenario[negativeIndex + 1]);

        Assert.True(negative.HasInterestRows);
        Assert.All(negative.InterestRows,
            item => Assert.NotEqual(0m, item.Amount));
        Assert.False(negative.HasDeficit);
        Assert.True(carry.HasDeficit);
        Assert.Equal(
            result.Scenario[negativeIndex + 1].CarryOverDeficit,
            carry.Deficit!.OpeningDeficit);
    }

    [Fact]
    public void Build_TransitionSectionOmitsZeroRows()
    {
        var source = _projection.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            1)[0];
        var transition = source with
        {
            IsStrategyTransition = true,
            TransitionCatchUpAmount = 0m,
            ForwardFundedAmount = 34_200m,
            MandatoryOutflow = 63_900m
        };

        var detail = _presenter.Build(transition);

        Assert.True(detail.HasTransitionRows);
        Assert.Collection(
            detail.TransitionRows,
            row => Assert.Equal("Yeni dönem için ayrılacak", row.Label),
            row => Assert.Equal("Toplam geçiş yükü", row.Label));
        Assert.DoesNotContain(detail.TransitionRows,
            row => row.Amount == 0m);
    }

    private static ObligationItem Payment(
        string name,
        ObligationType type,
        DateOnly dueDate,
        decimal amount,
        DateOnly assignedSalary,
        bool beforeSalary = false,
        bool isEstimate = false) =>
        new(
            name,
            type,
            dueDate,
            amount,
            IsEstimate: isEstimate,
            AssignedSalaryDate: assignedSalary,
            PaymentBeforeSalary: beforeSalary);
}
