using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class SalaryPeriodAndIncomeTests
{
    private readonly SalaryPeriodCalculator _periods = new();

    [Fact]
    public void SalaryPeriod_UsesInclusiveStartAndExclusiveEnd()
    {
        var period = _periods.GetPeriod(new DateOnly(2026, 9, 10), 10);

        Assert.Equal(new DateOnly(2026, 9, 10), period.Start);
        Assert.Equal(new DateOnly(2026, 10, 10), period.End);
        Assert.True(period.Contains(new DateOnly(2026, 9, 10)));
        Assert.True(period.Contains(new DateOnly(2026, 10, 9)));
        Assert.False(period.Contains(new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void DayBeforeSalary_BelongsToPreviousPeriod()
    {
        var period = _periods.GetPeriod(new DateOnly(2026, 9, 9), 10);

        Assert.Equal(new DateOnly(2026, 8, 10), period.Start);
        Assert.Equal(new DateOnly(2026, 9, 10), period.End);
    }

    [Theory]
    [InlineData(31, 2027, 2, 28)]
    [InlineData(31, 2028, 2, 29)]
    [InlineData(31, 2027, 4, 30)]
    [InlineData(30, 2027, 2, 28)]
    [InlineData(29, 2027, 2, 28)]
    public void MissingSalaryDay_UsesMonthEnd(
        int salaryDay,
        int year,
        int month,
        int expectedDay)
    {
        Assert.Equal(
            new DateOnly(year, month, expectedDay),
            CalendarRules.ResolveDay(year, month, salaryDay));
    }

    [Fact]
    public void PeriodSeries_RestoresPreferredDayAfterShortMonth()
    {
        var periods = _periods.GetPeriods(
            new DateOnly(2027, 1, 31),
            31,
            3);

        Assert.Equal(new DateOnly(2027, 1, 31), periods[0].Start);
        Assert.Equal(new DateOnly(2027, 2, 28), periods[1].Start);
        Assert.Equal(new DateOnly(2027, 3, 31), periods[2].Start);
    }

    [Fact]
    public void SalaryResolver_UsesLatestRecordEffectiveAtPeriodStart()
    {
        var salaries = new[]
        {
            Salary(115_000m, new DateOnly(2026, 1, 1)),
            Salary(132_250m, new DateOnly(2027, 1, 1))
        };
        var resolver = new SalaryResolver();

        Assert.Equal(
            115_000m,
            resolver.Resolve(new DateOnly(2026, 12, 10), salaries)!.Amount);
        Assert.Equal(
            132_250m,
            resolver.Resolve(new DateOnly(2027, 1, 10), salaries)!.Amount);
    }

    [Fact]
    public void SalaryChangeEffectiveMidPeriod_DoesNotChangeCurrentPeriod()
    {
        var resolver = new SalaryResolver();
        var salary = resolver.Resolve(
            new DateOnly(2026, 12, 10),
            [
                Salary(115_000m, new DateOnly(2026, 1, 1)),
                Salary(132_250m, new DateOnly(2027, 1, 1))
            ]);

        Assert.Equal(115_000m, salary!.Amount);
    }

    [Fact]
    public void OneTimeIncome_IsAssignedByExactDate()
    {
        var calculator = new IncomeProjectionCalculator(
            new SalaryResolver());
        var period = new SalaryPeriod(
            new DateOnly(2027, 3, 10),
            new DateOnly(2027, 4, 10));

        var result = calculator.Calculate(
            period,
            [Salary(100_000m, new DateOnly(2026, 1, 1))],
            [
                new OneTimeIncome
                {
                    Amount = 50_000m,
                    ExactDate = new DateOnly(2027, 3, 15)
                },
                new OneTimeIncome
                {
                    Amount = 75_000m,
                    ExactDate = new DateOnly(2027, 4, 10)
                }
            ]);

        Assert.Equal(100_000m, result.SalaryIncome);
        Assert.Equal(50_000m, result.OtherIncome);
        Assert.Equal(150_000m, result.TotalIncome);
    }

    [Fact]
    public void TargetAmount_ReturnsFirstReachedPeriod()
    {
        var rows = new[] { 100_000m, 180_000m, 270_000m, 340_000m }
            .Select((ending, index) => Projection(index, ending))
            .ToArray();

        var reached = new TargetAmountCalculator()
            .FindFirstReached(rows, 300_000m);

        Assert.NotNull(reached);
        Assert.Equal(new DateOnly(2027, 4, 10), reached!.PeriodStart);
    }

    private static SalaryScheduleEntry Salary(
        decimal amount,
        DateOnly effectiveDate) => new()
    {
        Amount = amount,
        EffectiveDate = effectiveDate
    };

    private static SalaryPeriodProjection Projection(
        int index,
        decimal ending) => new(
        new DateOnly(2027, 1, 10).AddMonths(index),
        new DateOnly(2027, 2, 10).AddMonths(index),
        0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m,
        0m, 0m, ending, false, false, false, [], [], [], []);
}

