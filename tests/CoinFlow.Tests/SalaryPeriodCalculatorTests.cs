using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class SalaryPeriodCalculatorTests
{
    private readonly SalaryPeriodCalculator _calculator = new();

    [Fact]
    public void SeptemberPeriod_UsesRealThirtyDayCalendar()
    {
        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 18), 10);

        Assert.Equal(new DateOnly(2026, 9, 10), period.Start);
        Assert.Equal(new DateOnly(2026, 10, 10), period.End);
        Assert.Equal(30, period.DayCount);
    }

    [Fact]
    public void DateBeforeSalaryDay_BelongsToPreviousPeriod()
    {
        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 9), 10);

        Assert.Equal(new DateOnly(2026, 8, 10), period.Start);
        Assert.Equal(new DateOnly(2026, 9, 10), period.End);
    }

    [Fact]
    public void SalaryDay31_ClampsFebruaryInLeapYear()
    {
        var period = _calculator.GetPeriod(new DateOnly(2028, 2, 29), 31);

        Assert.Equal(new DateOnly(2028, 2, 29), period.Start);
        Assert.Equal(new DateOnly(2028, 3, 31), period.End);
    }

    [Fact]
    public void SalaryDay31_ClampsFebruaryInCommonYear()
    {
        var period = _calculator.GetPeriod(new DateOnly(2027, 2, 28), 31);

        Assert.Equal(new DateOnly(2027, 2, 28), period.Start);
        Assert.Equal(new DateOnly(2027, 3, 31), period.End);
    }

    [Theory]
    [InlineData(29, "2027-02-28")]
    [InlineData(30, "2027-02-28")]
    [InlineData(31, "2027-02-28")]
    public void SalaryDaysMissingInFebruary_UseLastValidDay(int salaryDay, string expected)
    {
        var resolved = CalendarRules.ResolveDay(2027, 2, salaryDay);
        Assert.Equal(DateOnly.Parse(expected), resolved);
    }

    [Fact]
    public void PaymentOnSalaryDay_IsIncludedInNewPeriod()
    {
        var salary = new SalaryScheduleEntry { NetAmount = 10_000m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var loan = new Loan
        {
            Name = "Test",
            MonthlyInstallment = 1_000m,
            PaymentDay = 10,
            StartDate = new DateOnly(2026, 9, 10),
            InstallmentCount = 2
        };

        var result = _calculator.Calculate(new SalaryPeriodRequest(
            new DateOnly(2026, 9, 10), 10, [salary], [loan], [], []));

        Assert.Single(result.Obligations);
        Assert.Equal(new DateOnly(2026, 9, 10), result.Obligations[0].DueDate);
        Assert.Equal(9_000m, result.SpendableBudget);
    }

    [Fact]
    public void PaymentOnNextSalaryDay_IsExcludedFromCurrentPeriod()
    {
        var salary = new SalaryScheduleEntry { NetAmount = 10_000m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var obligation = new ObligationItem("Kart", ObligationType.CreditCard, new DateOnly(2026, 10, 10), 2_000m);

        var result = _calculator.Calculate(new SalaryPeriodRequest(
            new DateOnly(2026, 9, 10), 10, [salary], [], [], [obligation]));

        Assert.Empty(result.Obligations);
        Assert.Equal(10_000m, result.SpendableBudget);
    }

    [Fact]
    public void RaiseEffectiveInsidePeriod_AppliesAtNextSalaryOnly()
    {
        var schedule = new[]
        {
            new SalaryScheduleEntry { NetAmount = 100m, EffectiveFrom = new DateOnly(2026, 1, 1) },
            new SalaryScheduleEntry { NetAmount = 115m, EffectiveFrom = new DateOnly(2026, 9, 15) }
        };

        Assert.Equal(100m, _calculator.ResolveSalary(new DateOnly(2026, 9, 10), schedule));
        Assert.Equal(115m, _calculator.ResolveSalary(new DateOnly(2026, 10, 10), schedule));
    }

    [Fact]
    public void ClampedCalendarCanContainTwoRealMonthlyDueDates()
    {
        var loan = new Loan
        {
            Name = "Ay sonu",
            MonthlyInstallment = 100m,
            PaymentDay = 30,
            StartDate = new DateOnly(2027, 1, 30),
            EndDate = new DateOnly(2027, 4, 30)
        };
        var dates = _calculator.GetLoanDates(loan);
        var period = _calculator.GetPeriod(new DateOnly(2027, 2, 28), 31);

        Assert.Equal(2, dates.Count(period.Contains));
        Assert.Contains(new DateOnly(2027, 2, 28), dates);
        Assert.Contains(new DateOnly(2027, 3, 30), dates);
    }

    [Fact]
    public void LastLoanInstallment_IsMarkedFinal()
    {
        var loan = new Loan
        {
            Name = "Kısa kredi",
            MonthlyInstallment = 500m,
            PaymentDay = 15,
            StartDate = new DateOnly(2026, 8, 15),
            InstallmentCount = 2
        };
        var salary = new SalaryScheduleEntry { NetAmount = 10_000m, EffectiveFrom = new DateOnly(2026, 1, 1) };

        var result = _calculator.Calculate(new SalaryPeriodRequest(
            new DateOnly(2026, 9, 10), 10, [salary], [loan], [], []));

        Assert.True(Assert.Single(result.Obligations).IsFinalPayment);
    }
}
