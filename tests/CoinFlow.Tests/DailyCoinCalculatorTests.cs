using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class DailyCoinCalculatorTests
{
    private readonly DailyCoinCalculator _calculator = new();
    private readonly SalaryPeriod _period = new(new DateOnly(2026, 8, 10), new DateOnly(2026, 9, 10));

    [Fact]
    public void RemainingElevenThousandAcrossTwentyTwoDays_IsFiveHundredDaily()
    {
        var expenses = new[]
        {
            Cash(4_200m, new DateOnly(2026, 8, 10)),
            Cash(8_000m, new DateOnly(2026, 8, 13)),
            Cash(4_033m, new DateOnly(2026, 8, 18))
        };

        var result = _calculator.Calculate(_period, new DateOnly(2026, 8, 19), 27_233m, expenses);

        Assert.Equal(11_000m, result.RemainingBudget);
        Assert.Equal(22, result.RemainingDays);
        Assert.Equal(500m, result.SustainableDailyBudget);
    }

    [Fact]
    public void UnspentDailyCoin_AccumulatesInPool()
    {
        var shortPeriod = new SalaryPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 11));
        var expenses = new[] { Cash(100m, new DateOnly(2026, 1, 1)) };

        var result = _calculator.Calculate(shortPeriod, new DateOnly(2026, 1, 3), 5_000m, expenses);

        Assert.Equal(500m, result.BaseDailyCoin);
        Assert.Equal(1_400m, result.CoinPool);
    }

    [Fact]
    public void TodayEarned_IsDailyCoinMinusTodayCashExpense()
    {
        var shortPeriod = new SalaryPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 11));
        var result = _calculator.Calculate(shortPeriod, new DateOnly(2026, 1, 1), 5_000m,
            [Cash(300m, new DateOnly(2026, 1, 1))]);

        Assert.Equal(200m, result.TodayEarned);
        Assert.Equal(200m, result.CoinPool);
    }

    [Theory]
    [InlineData(ExpensePaymentType.CreditCard)]
    [InlineData(ExpensePaymentType.NewInstallment)]
    public void DeferredPayment_DoesNotReduceCurrentCashPool(ExpensePaymentType paymentType)
    {
        var shortPeriod = new SalaryPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 11));
        var expense = Cash(1_000m, new DateOnly(2026, 1, 1)) with { PaymentType = paymentType };

        var result = _calculator.Calculate(shortPeriod, new DateOnly(2026, 1, 1), 5_000m, [expense]);

        Assert.Equal(0m, result.PeriodCashSpending);
        Assert.Equal(500m, result.CoinPool);
    }

    [Fact]
    public void Overspending_IsRepresentedWithoutClamping()
    {
        var shortPeriod = new SalaryPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 11));
        var result = _calculator.Calculate(shortPeriod, new DateOnly(2026, 1, 2), 1_000m,
            [Cash(1_500m, new DateOnly(2026, 1, 2))]);

        Assert.Equal(-1_300m, result.CoinPool);
        Assert.Equal(-500m, result.RemainingBudget);
    }

    [Fact]
    public void DateOutsidePeriod_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(_period, _period.End, 100m, []));
    }

    private static Expense Cash(decimal amount, DateOnly date) => new()
    {
        Amount = amount,
        Date = date,
        PaymentType = ExpensePaymentType.Cash
    };
}
