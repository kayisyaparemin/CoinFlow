using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class SimulatorInsightServiceTests
{
    private readonly SimulatorInsightService _service = new();

    [Fact]
    public void PeriodNeed_ComposesCashRequirementsWithoutOpeningState()
    {
        var row = Row(
            new DateOnly(2027, 3, 10),
            opening: 50_000m,
            income: 100_000m,
            loan: 10_000m,
            creditCard: 20_000m,
            temporary: 5_000m,
            installment: 7_000m,
            other: 3_000m,
            living: 30_000m,
            large: 15_000m,
            deficitInterest: 1_000m,
            ending: 59_000m);

        var view = _service.Build([row]).Periods[0];

        Assert.Equal(91_000m, view.NeedTotal);
        Assert.Equal(9_000m, view.IncomeCoverage);
        Assert.Equal("Gelirlerden kalan", view.CoverageLabel);
        Assert.DoesNotContain(
            view.NeedBreakdown,
            x => x.Label.Contains("Dönem başı", StringComparison.Ordinal));
        Assert.Equal(91_000m,
            view.NeedBreakdown.Single(x => x.Label == "Toplam").Amount);
    }

    [Fact]
    public void IncomeInsufficient_DoesNotBecomeFinancingDeficitWhenOpeningCoversGap()
    {
        var row = Row(
            new DateOnly(2027, 3, 10),
            opening: 50_000m,
            income: 100_000m,
            mandatory: 90_000m,
            living: 30_000m,
            ending: 30_000m);

        var summary = _service.Build([row]);
        var view = summary.Periods[0];

        Assert.Equal(120_000m, view.NeedTotal);
        Assert.Equal(-20_000m, view.IncomeCoverage);
        Assert.Equal("Gelirlerin karşılamadığı", view.CoverageLabel);
        Assert.NotNull(summary.FirstIncomeInsufficientPeriod);
        Assert.Null(summary.FirstDeficitPeriod);
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("dönem başı durumundan", StringComparison.Ordinal));
        Assert.DoesNotContain(
            summary.NarrativeInsights,
            x => x.Contains("Mevcut", StringComparison.OrdinalIgnoreCase) ||
                 x.Contains("Yeni Plan", StringComparison.OrdinalIgnoreCase) ||
                 x.Contains("fark", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FinancingDeficit_ReportsRecoveryInsideHorizon()
    {
        var september = Row(
            new DateOnly(2026, 9, 10),
            opening: 0m,
            income: 50_000m,
            mandatory: 40_000m,
            living: 35_000m,
            deficitInterest: 1_250m,
            ending: -26_250m);
        var october = Row(
            new DateOnly(2026, 10, 10),
            opening: -26_250m,
            income: 70_000m,
            mandatory: 20_000m,
            living: 40_000m,
            ending: -16_250m);
        var november = Row(
            new DateOnly(2026, 11, 10),
            opening: -16_250m,
            income: 80_000m,
            mandatory: 10_000m,
            living: 30_000m,
            ending: 23_750m);

        var summary = _service.Build([september, october, november]);

        Assert.Equal(september.PeriodStart,
            summary.FirstDeficitPeriod?.Projection.PeriodStart);
        Assert.Equal(november.PeriodStart,
            summary.DeficitRecoveryPeriod?.Projection.PeriodStart);
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("finansman açığı oluşuyor", StringComparison.Ordinal));
        Assert.Contains(
            summary.NarrativeInsights,
            x => x.Contains("kapanması bekleniyor", StringComparison.Ordinal));
    }

    [Fact]
    public void HighestNeedLowestEndingAndRecovery_AreDeterministicAndDeduplicated()
    {
        var january = Row(
            new DateOnly(2027, 1, 10),
            opening: 70_000m,
            income: 100_000m,
            mandatory: 40_000m,
            living: 30_000m,
            ending: 100_000m);
        var february = Row(
            new DateOnly(2027, 2, 10),
            opening: 100_000m,
            income: 100_000m,
            mandatory: 95_000m,
            living: 45_000m,
            ending: 60_000m);
        var march = Row(
            new DateOnly(2027, 3, 10),
            opening: 60_000m,
            income: 100_000m,
            mandatory: 40_000m,
            living: 30_000m,
            ending: 90_000m);
        var april = Row(
            new DateOnly(2027, 4, 10),
            opening: 90_000m,
            income: 100_000m,
            mandatory: 30_000m,
            living: 30_000m,
            ending: 130_000m);

        var summary = _service.Build([january, february, march, april]);

        Assert.Equal(february.PeriodStart,
            summary.HighestNeedPeriod.Projection.PeriodStart);
        Assert.Equal(february.PeriodStart,
            summary.LowestEndingPeriod.Projection.PeriodStart);
        Assert.Equal(march.PeriodStart,
            summary.BurdenReliefPeriod?.Projection.PeriodStart);
        Assert.All(summary.Periods,
            period => Assert.True(period.InsightChips.Count <= 2));
    }

    [Fact]
    public void RepresentativeTimelineStory_DoesNotNarrateScenarioInputs()
    {
        var plan = TestFactory.CanonicalPlan();
        var card = Assert.Single(plan.CreditCards);
        var calculator = new SimulationCalculator(
            TestFactory.ProjectionCalculator(),
            new InstallmentScheduleCalculator());
        var result = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            new SimulationRequest(
                SimulationScenarioType.CreditCardInstallmentPurchase,
                "Beyaz eşya",
                120_000m,
                new DateOnly(2026, 9, 24),
                9,
                CreditCardId: card.Id));

        var summary = _service.Build(result.Scenario);
        var text = string.Join(" ", summary.NarrativeInsights);

        Assert.DoesNotContain("ekledin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taksit ek", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mevcut Plan", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yeni Plan", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(summary.NarrativeInsights);
        Assert.NotEmpty(summary.Periods);
    }

    private static SalaryPeriodProjection Row(
        DateOnly start,
        decimal opening,
        decimal income,
        decimal mandatory = 0m,
        decimal loan = 0m,
        decimal creditCard = 0m,
        decimal temporary = 0m,
        decimal installment = 0m,
        decimal other = 0m,
        decimal living = 0m,
        decimal large = 0m,
        decimal deficitInterest = 0m,
        decimal ending = 0m)
    {
        var mandatoryTotal = mandatory == 0m
            ? loan + creditCard + temporary + installment + other
            : mandatory;
        var end = CalendarRules.AddMonthsKeepingDay(start, 1, 10);
        return new SalaryPeriodProjection(
            start,
            end,
            income,
            0m,
            income,
            loan,
            creditCard,
            temporary,
            installment,
            other,
            mandatoryTotal,
            income - mandatoryTotal,
            living,
            income - mandatoryTotal - living - large,
            large,
            opening,
            ending,
            IsEstimatedCardPayment: false,
            HasUndeterminedCardPayment: false,
            HasDeficit: ending < 0m,
            IncomeItems: [],
            MandatoryItems: [],
            LargeExpenseItems: [],
            CardPaymentStatuses: [],
            PaymentAssignmentMode: PaymentAssignmentMode.UpcomingPeriod,
            PaymentWindowStart: start,
            PaymentWindowEnd: end.AddDays(-1),
            EndingProjectedSavingsBeforeDeficitInterest:
                ending + deficitInterest,
            DeficitFinancingInterest: deficitInterest);
    }
}
