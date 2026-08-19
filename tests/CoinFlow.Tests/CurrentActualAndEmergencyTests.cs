using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CurrentActualAndEmergencyTests
{
    private static readonly SalaryPeriod Period = new(
        new DateOnly(2026, 8, 10),
        new DateOnly(2026, 9, 10));
    private static readonly DateTimeOffset SnapshotCreated = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SnapshotElevenThousandAcrossTwentyTwoDays_IsFiveHundred()
    {
        var balance = new SpendableBalanceCalculator().Calculate(
            Period,
            new DateOnly(2026, 8, 19),
            86_418.06m,
            new DateOnly(2026, 8, 19),
            [Snapshot(11_000m)],
            []);

        var daily = new DailyCoinCalculator().Calculate(
            Period,
            new DateOnly(2026, 8, 19),
            86_418.06m,
            balance);

        Assert.Equal(11_000m, daily.RemainingBudget);
        Assert.Equal(22, daily.RemainingDays);
        Assert.Equal(500m, daily.BaseDailyCoin);
        Assert.Equal(500m, daily.SustainableDailyBudget);
    }

    [Fact]
    public void CashExpenseAfterSnapshot_ReducesCurrentActual()
    {
        var expense = Expense(1_500m, ExpensePaymentType.Cash, SnapshotCreated.AddMinutes(1));

        var result = CalculateBalance([expense]);

        Assert.Equal(9_500m, result.CurrentAvailable);
    }

    [Theory]
    [InlineData(ExpensePaymentType.CreditCard)]
    [InlineData(ExpensePaymentType.NewInstallment)]
    public void DeferredExpense_DoesNotReduceCurrentActual(ExpensePaymentType type)
    {
        var expense = Expense(1_500m, type, SnapshotCreated.AddMinutes(1));

        var result = CalculateBalance([expense]);

        Assert.Equal(11_000m, result.CurrentAvailable);
    }

    [Fact]
    public void ExpenseRecordedBeforeSameDaySnapshot_IsNotSubtractedAgain()
    {
        var expense = Expense(1_500m, ExpensePaymentType.Cash, SnapshotCreated.AddMinutes(-1));

        var result = CalculateBalance([expense]);

        Assert.Equal(11_000m, result.CurrentAvailable);
    }

    [Fact]
    public void NegativeCurrentBalance_IsNotClamped()
    {
        var expense = Expense(12_000m, ExpensePaymentType.Cash, SnapshotCreated.AddMinutes(1));

        var result = CalculateBalance([expense]);

        Assert.Equal(-1_000m, result.CurrentAvailable);
    }

    [Fact]
    public void MissingSnapshot_WhenTrackingStartedMidPeriod_RequiresUserInput()
    {
        var result = new SpendableBalanceCalculator().Calculate(
            Period,
            new DateOnly(2026, 8, 19),
            80_000m,
            new DateOnly(2026, 8, 19),
            [],
            []);

        Assert.True(result.RequiresSnapshot);
        Assert.Equal(SpendableBalanceSource.Missing, result.Source);
    }

    [Fact]
    public void TrackingFromPeriodStart_CanUseProjectedBudgetFallback()
    {
        var result = new SpendableBalanceCalculator().Calculate(
            Period,
            new DateOnly(2026, 8, 19),
            80_000m,
            Period.Start,
            [],
            [Expense(5_000m, ExpensePaymentType.Cash, SnapshotCreated.AddMinutes(1))]);

        Assert.False(result.RequiresSnapshot);
        Assert.Equal(75_000m, result.CurrentAvailable);
        Assert.Equal(SpendableBalanceSource.PeriodStart, result.Source);
    }

    [Fact]
    public void PlannedEmergencyTransfer_IsNotChargedToSpendableTwice()
    {
        var calculator = new EmergencyFundCalculator();
        var fund = new EmergencyFund
        {
            TargetAmount = 150_000m,
            CurrentAmount = 100_000m,
            PlannedPeriodContribution = 20_000m
        };

        var allocation = calculator.AllocateTransfer(fund, Period.Start, 20_000m, []);

        Assert.Equal(20_000m, allocation.CoveredPlannedAmount);
        Assert.Equal(0m, allocation.ExtraSpendableAmount);
    }

    [Fact]
    public void EmergencyContribution_IsCappedAtRemainingTarget()
    {
        var calculator = new EmergencyFundCalculator();
        var fund = new EmergencyFund
        {
            TargetAmount = 150_000m,
            CurrentAmount = 145_000m,
            PlannedPeriodContribution = 20_000m
        };

        var result = calculator.CalculateCurrentPeriod(fund, Period.Start, []);

        Assert.Equal(5_000m, result.ReservedAmount);
    }

    [Fact]
    public void TransferBeyondReservedAmount_OnlyChargesExtraToSpendable()
    {
        var calculator = new EmergencyFundCalculator();
        var fund = new EmergencyFund
        {
            TargetAmount = 200_000m,
            CurrentAmount = 100_000m,
            PlannedPeriodContribution = 20_000m
        };

        var result = calculator.AllocateTransfer(fund, Period.Start, 25_000m, []);

        Assert.Equal(20_000m, result.CoveredPlannedAmount);
        Assert.Equal(5_000m, result.ExtraSpendableAmount);
    }

    private static SpendableBalanceState CalculateBalance(IEnumerable<Expense> expenses) =>
        new SpendableBalanceCalculator().Calculate(
            Period,
            new DateOnly(2026, 8, 19),
            86_418.06m,
            new DateOnly(2026, 8, 19),
            [Snapshot(11_000m)],
            expenses);

    private static SpendableBalanceSnapshot Snapshot(decimal amount) => new()
    {
        Amount = amount,
        SnapshotDate = new DateOnly(2026, 8, 19),
        SalaryPeriodStart = Period.Start,
        CreatedAtUtc = SnapshotCreated
    };

    private static Expense Expense(
        decimal amount,
        ExpensePaymentType type,
        DateTimeOffset createdAt) => new()
    {
        Amount = amount,
        Date = new DateOnly(2026, 8, 19),
        PaymentType = type,
        CreatedAtUtc = createdAt
    };
}
