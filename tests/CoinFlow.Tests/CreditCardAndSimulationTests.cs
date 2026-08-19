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
    public void Simulation_PreservesTotalUsingLastInstallmentRemainder()
    {
        var baseline = Enumerable.Range(0, 3)
            .Select(i => new FutureMonthProjection(
                new SalaryPeriod(new DateOnly(2026, 12, 10).AddMonths(i), new DateOnly(2027, 1, 10).AddMonths(i)),
                100_000m, 0m, 0m, 0m, 0m, 0m, 0m, 100_000m, []))
            .ToArray();
        var calculator = new PurchaseSimulationCalculator();

        var rows = calculator.Calculate(
            new PurchaseSimulationRequest("Test", 100m, 3, new DateOnly(2026, 12, 1)), baseline);

        Assert.Equal(100m, rows.Sum(x => x.NewInstallment));
        Assert.Equal(33.34m, rows[^1].NewInstallment);
    }

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
