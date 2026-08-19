using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardAndSimulationTests
{
    private readonly CreditCardProjectionCalculator _cardCalculator = new();
    private readonly InstallmentScheduleCalculator _installments = new();

    [Fact]
    public void CarriedThirtyFivePlusUnbilledFiftyNine_ProducesNinetyFourStatementAndThirtySevenSixPayment()
    {
        var card = CreateCard() with
        {
            CarriedBalance = 35_000m,
            UnbilledSpending = 59_000m,
            PaymentStrategy = CreditCardPaymentStrategy.Minimum
        };

        var statement = Assert.Single(_cardCalculator.Project(card, 1));

        Assert.Equal(94_000m, statement.StatementBalance);
        Assert.Equal(37_600m, statement.Payment);
        Assert.Equal(56_400m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void ChargeOnSeptember24_EntersSeptember25Statement()
    {
        var card = CreateCard() with
        {
            Charges = [Charge(new DateOnly(2026, 9, 24), 1_000m)]
        };

        var september = _cardCalculator.Project(card, 2)
            .Single(x => x.StatementCloseDate == new DateOnly(2026, 9, 25));

        Assert.Equal(1_000m, september.NewCharges);
    }

    [Fact]
    public void ChargeOnSeptember28_EntersOctober25Statement()
    {
        var card = CreateCard() with
        {
            Charges = [Charge(new DateOnly(2026, 9, 28), 1_000m)]
        };

        var statements = _cardCalculator.Project(card, 3);

        Assert.Equal(0m, statements.Single(x => x.StatementCloseDate == new DateOnly(2026, 9, 25)).NewCharges);
        Assert.Equal(1_000m, statements.Single(x => x.StatementCloseDate == new DateOnly(2026, 10, 25)).NewCharges);
    }

    [Fact]
    public void September25Statement_IsDueOctober5()
    {
        var due = CreditCardProjectionCalculator.ResolvePaymentDueDate(
            new DateOnly(2026, 9, 25),
            5);

        Assert.Equal(new DateOnly(2026, 10, 5), due);
    }

    [Fact]
    public void DueDayAfterClose_CanRemainInSameMonth()
    {
        var due = CreditCardProjectionCalculator.ResolvePaymentDueDate(
            new DateOnly(2026, 9, 25),
            28);

        Assert.Equal(new DateOnly(2026, 9, 28), due);
    }

    [Fact]
    public void October5CardPayment_BelongsToSeptember10SalaryPeriod()
    {
        var period = new SalaryPeriod(new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 10));

        Assert.True(period.Contains(new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public void ManualPayment_IsAppliedOnlyToItsExactDueDate()
    {
        var card = CreateCard() with
        {
            CarriedBalance = 100_000m,
            PaymentStrategy = CreditCardPaymentStrategy.Minimum,
            PaymentPlans =
            [
                new CreditCardPaymentPlan
                {
                    DueDate = new DateOnly(2026, 11, 5),
                    PaymentType = CreditCardPaymentType.FixedAmount,
                    Amount = 50_000m
                }
            ]
        };

        var statements = _cardCalculator.Project(card, 3);

        Assert.Equal(40_000m, statements[0].Payment);
        Assert.Equal(50_000m, statements[1].Payment);
        Assert.Equal(4_000m, statements[2].Payment);
    }

    [Fact]
    public void CurrentCardDebt_IsDerivedFromAllOutstandingComponents()
    {
        var card = CreateCard() with
        {
            CarriedBalance = 35_201.77m,
            UnbilledSpending = 61_283.91m,
            Charges =
            [
                Charge(new DateOnly(2026, 9, 28), 15_538.36m),
                Charge(new DateOnly(2026, 10, 30), 9_102.90m),
                Charge(new DateOnly(2026, 11, 28), 2_624.55m)
            ]
        };

        Assert.Equal(123_751.49m, CreditCardProjectionCalculator.DeriveCurrentTotalDebt(card));
    }

    [Fact]
    public void InstallmentRounding_PreservesOriginalTotal()
    {
        var schedule = _installments.Split(100m, 3, new DateOnly(2026, 12, 15));

        Assert.Equal(100m, schedule.Sum(x => x.Amount));
        Assert.Equal(33.34m, schedule[^1].Amount);
    }

    [Fact]
    public void CardPurchase3790AcrossFourMonths_ProducesExact94750PostingCharges()
    {
        var schedule = _installments.Split(3_790m, 4, new DateOnly(2026, 9, 24));

        Assert.All(schedule, item => Assert.Equal(947.50m, item.Amount));
        Assert.Equal(
            [
                new DateOnly(2026, 9, 24),
                new DateOnly(2026, 10, 24),
                new DateOnly(2026, 11, 24),
                new DateOnly(2026, 12, 24)
            ],
            schedule.Select(x => x.Date));
    }

    [Fact]
    public void CurrentPeriodCashSimulation_StartsFromActualRemaining()
    {
        var calculator = new PurchaseSimulationCalculator(_cardCalculator, _installments);
        var baseline = CreateBaseline(new DateOnly(2026, 8, 10), 2, firstActual: 11_000m);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Yakıt", 3_500m, PurchaseFundingMethod.Cash,
                new DateOnly(2026, 8, 20), 1, new DateOnly(2026, 8, 20)),
            baseline,
            []);

        Assert.True(result.Rows[0].UsesCurrentActual);
        Assert.Equal(11_000m, result.Rows[0].BaselineSpendable);
        Assert.Equal(7_500m, result.Rows[0].ResultingSpendable);
    }

    [Fact]
    public void FuturePeriodCashSimulation_StartsFromProjectedBudget()
    {
        var calculator = new PurchaseSimulationCalculator(_cardCalculator, _installments);
        var baseline = CreateBaseline(new DateOnly(2026, 8, 10), 2, firstActual: 11_000m);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Telefon", 30_000m, PurchaseFundingMethod.Cash,
                new DateOnly(2026, 9, 20), 1, new DateOnly(2026, 9, 20)),
            baseline,
            []);

        Assert.False(result.Rows[1].UsesCurrentActual);
        Assert.Equal(80_000m, result.Rows[1].BaselineSpendable);
        Assert.Equal(50_000m, result.Rows[1].ResultingSpendable);
    }

    [Fact]
    public void CreditCardSimulation_UsesExactPostingDatesAndSharedStatementEngine()
    {
        var card = CreateCard() with
        {
            Limit = 200_000m,
            CurrentTotalDebt = 0m,
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum
        };
        var calculator = new PurchaseSimulationCalculator(_cardCalculator, _installments);
        var baseline = CreateBaseline(new DateOnly(2026, 8, 10), 12, firstActual: 11_000m);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Alışveriş", 3_790m, PurchaseFundingMethod.CreditCard,
                new DateOnly(2026, 9, 24), 4, new DateOnly(2026, 10, 5), card.Id),
            baseline,
            [card]);

        Assert.Contains(result.Rows, x => x.NewPayment > 0m);
        Assert.Equal(3_790m, result.PurchaseAmount);
        Assert.Contains("exact posting", result.Explanation);
    }

    private static FutureMonthProjection[] CreateBaseline(
        DateOnly periodStart,
        int count,
        decimal? firstActual) => Enumerable.Range(0, count)
        .Select(index => new FutureMonthProjection(
            new SalaryPeriod(periodStart.AddMonths(index), periodStart.AddMonths(index + 1)),
            100_000m,
            20_000m,
            0m,
            0m,
            0m,
            0m,
            20_000m,
            80_000m,
            index == 0 ? firstActual : null,
            2_666.67m,
            [],
            []))
        .ToArray();

    private static CreditCard CreateCard() => new()
    {
        Name = "Test",
        BalanceAsOfDate = new DateOnly(2026, 8, 26),
        StatementClosingDay = 25,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m
    };

    private static CardCharge Charge(DateOnly date, decimal amount) => new()
    {
        PostingDate = date,
        Amount = amount
    };
}
