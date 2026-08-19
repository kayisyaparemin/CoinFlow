using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardAndSimulationTests
{
    private readonly CreditCardProjectionCalculator _cardCalculator = new();

    [Fact]
    public void MinimumPayment_UsesStatementBalanceAndRate()
    {
        var card = CreateCard();
        var month = Assert.Single(_cardCalculator.Project(card, new DateOnly(2026, 9, 1), 1));

        Assert.Equal(37_600m, month.Payment);
        Assert.Equal(56_400m, month.ClosingBalance);
    }

    [Fact]
    public void ManualPayment_AppliesOnlyToFirstProjectionMonth()
    {
        var card = CreateCard() with
        {
            PaymentMode = CreditCardPaymentMode.Manual,
            ManualPaymentAmount = 45_000m
        };
        var months = _cardCalculator.Project(card, new DateOnly(2026, 9, 1), 2);

        Assert.Equal(45_000m, months[0].Payment);
        Assert.Equal(19_600m, months[1].Payment);
    }

    [Fact]
    public void FutureInstallment_IsAddedInMatchingMonth()
    {
        var card = CreateCard() with
        {
            FutureInstallments =
            [
                new CardInstallment { DueDate = new DateOnly(2026, 10, 1), Amount = 8_000m }
            ]
        };

        var months = _cardCalculator.Project(card, new DateOnly(2026, 9, 1), 2);

        Assert.Equal(0m, months[0].NewCharges);
        Assert.Equal(8_000m, months[1].NewCharges);
        Assert.Equal(41_840m, months[1].ClosingBalance);
    }

    [Fact]
    public void CurrentCardDebt_IsDerivedFromVisibleFormComponents()
    {
        var card = CreateCard() with
        {
            LastStatementRemaining = 35_201.77m,
            CurrentCycleSpending = 61_283.91m,
            FutureInstallments =
            [
                new CardInstallment { Amount = 15_538.36m },
                new CardInstallment { Amount = 9_102.90m },
                new CardInstallment { Amount = 2_624.55m }
            ]
        };

        Assert.Equal(123_751.49m, CreditCardProjectionCalculator.DeriveCurrentTotalDebt(card));
    }

    [Fact]
    public void Simulation_PreservesTotalUsingLastInstallmentRemainder()
    {
        var baseline = CreateBaseline(new DateOnly(2026, 12, 10), 3);
        var calculator = new PurchaseSimulationCalculator(_cardCalculator);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Test", 100m, PurchaseFundingMethod.CashDebt,
                new DateOnly(2026, 12, 1), 3, new DateOnly(2026, 12, 15)),
            baseline,
            []);

        Assert.Equal(100m, result.Rows.Sum(x => x.NewPayment));
        Assert.Equal(33.34m, result.Rows[^1].NewPayment);
        Assert.Equal(0m, result.RemainingNewDebtAfterHorizon);
    }

    [Fact]
    public void CashSimulation_ReducesOnlyPurchaseSalaryPeriod()
    {
        var baseline = CreateBaseline(new DateOnly(2026, 12, 10), 2);
        var calculator = new PurchaseSimulationCalculator(_cardCalculator);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Telefon", 30_000m, PurchaseFundingMethod.Cash,
                new DateOnly(2026, 12, 20), 1, new DateOnly(2026, 12, 20)),
            baseline,
            []);

        Assert.Equal(30_000m, result.Rows[0].NewPayment);
        Assert.Equal(50_000m, result.Rows[0].ResultingSpendable);
        Assert.Equal(0m, result.Rows[1].NewPayment);
        Assert.Equal(20_000m, result.Rows[0].BaselineObligations);
    }

    [Theory]
    [InlineData(PurchaseFundingMethod.CashDebt)]
    [InlineData(PurchaseFundingMethod.BankLoan)]
    public void FinancedSimulation_UsesTotalRepaymentIncludingFinancingCost(PurchaseFundingMethod method)
    {
        var baseline = CreateBaseline(new DateOnly(2026, 12, 10), 3);
        var calculator = new PurchaseSimulationCalculator(_cardCalculator);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Araç", 100_000m, method,
                new DateOnly(2026, 12, 1), 3, new DateOnly(2026, 12, 15),
                TotalRepaymentAmount: 120_000m),
            baseline,
            []);

        Assert.All(result.Rows, row => Assert.Equal(40_000m, row.NewPayment));
        Assert.Equal(120_000m, result.TotalRepaymentAmount);
        Assert.Equal(120_000m, result.NewPaymentsInHorizon);
    }

    [Fact]
    public void CreditCardSimulation_UsesExistingBalanceAndMinimumPaymentProjection()
    {
        var card = CreateCard() with
        {
            Limit = 200_000m,
            CurrentTotalDebt = 100_000m,
            LastStatementDebt = 100_000m,
            LastStatementRemaining = 100_000m,
            FutureInstallments = []
        };
        var cardProjection = _cardCalculator.Project(card, new DateOnly(2026, 9, 5), 3);
        var baseline = cardProjection.Select((month, index) => new FutureMonthProjection(
            new SalaryPeriod(new DateOnly(2026, 9, 1).AddMonths(index), new DateOnly(2026, 10, 1).AddMonths(index)),
            100_000m,
            10_000m,
            month.Payment,
            0m,
            0m,
            0m,
            10_000m + month.Payment,
            90_000m - month.Payment,
            [])).ToArray();
        var calculator = new PurchaseSimulationCalculator(_cardCalculator);

        var result = calculator.Calculate(
            new PurchaseSimulationRequest(
                "Bilgisayar", 30_000m, PurchaseFundingMethod.CreditCard,
                new DateOnly(2026, 9, 10), 1, new DateOnly(2026, 10, 5), card.Id),
            baseline,
            [card]);

        Assert.Equal(0m, result.Rows[0].NewPayment);
        Assert.Equal(12_000m, result.Rows[1].NewPayment);
        Assert.Equal(result.Rows[1].BaselineObligations + 12_000m, result.Rows[1].ResultingObligations);
        Assert.True(result.RemainingNewDebtAfterHorizon > 0m);
    }

    private static FutureMonthProjection[] CreateBaseline(DateOnly periodStart, int count) =>
        Enumerable.Range(0, count)
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
                []))
            .ToArray();

    private static CreditCard CreateCard() => new()
    {
        Name = "Test",
        LastStatementDebt = 94_000m,
        LastStatementRemaining = 94_000m,
        StatementClosingDay = 25,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m,
        PaymentMode = CreditCardPaymentMode.Minimum
    };
}
