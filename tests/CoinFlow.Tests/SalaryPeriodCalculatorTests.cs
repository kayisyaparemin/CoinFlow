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
        var loan = new Loan
        {
            Name = "Test",
            MonthlyInstallment = 1_000m,
            PaymentDay = 10,
            StartDate = new DateOnly(2026, 9, 10),
            InstallmentCount = 2
        };

        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 10), 10);
        var dates = new LoanScheduleCalculator().GetPaymentDates(loan);

        Assert.Contains(new DateOnly(2026, 9, 10), dates.Where(period.Contains));
    }

    [Fact]
    public void PaymentOnNextSalaryDay_IsExcludedFromCurrentPeriod()
    {
        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 10), 10);

        Assert.False(period.Contains(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void TemporaryPayment_UsesExactDateForSalaryPeriod()
    {
        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 10), 10);
        var planId = Guid.NewGuid();
        var plan = new TemporaryPaymentPlan
        {
            Id = planId,
            Name = "Geçici",
            Installments =
            [
                new TemporaryPaymentInstallment
                {
                    PlanId = planId,
                    DueDate = new DateOnly(2026, 10, 9),
                    Amount = 500m
                },
                new TemporaryPaymentInstallment
                {
                    PlanId = planId,
                    DueDate = new DateOnly(2026, 10, 10),
                    Amount = 700m
                }
            ]
        };

        var result = new MandatoryPaymentCalculator(new LoanScheduleCalculator()).Calculate(
            period, [], [plan], [], 0m);

        Assert.Equal(500m, result.TemporaryPayments);
        Assert.Single(result.Items);
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
        var dates = new LoanScheduleCalculator().GetPaymentDates(loan);
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
        var period = _calculator.GetPeriod(new DateOnly(2026, 9, 10), 10);
        var result = new MandatoryPaymentCalculator(new LoanScheduleCalculator()).Calculate(
            period,
            [loan],
            [],
            [],
            0m);

        Assert.True(Assert.Single(result.Items).IsFinalPayment);
    }
}
