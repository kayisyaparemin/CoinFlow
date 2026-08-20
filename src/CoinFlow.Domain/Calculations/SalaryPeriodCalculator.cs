using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record SalaryPeriod(DateOnly Start, DateOnly End)
{
    public int DayCount => End.DayNumber - Start.DayNumber;
    public bool Contains(DateOnly date) => date >= Start && date < End;
}

public sealed class SalaryPeriodCalculator
{
    public SalaryPeriod GetPeriod(DateOnly date, int salaryDay)
    {
        CalendarRules.ValidateDay(salaryDay);
        var currentMonthSalaryDate = CalendarRules.ResolveDay(date.Year, date.Month, salaryDay);
        if (date >= currentMonthSalaryDate)
        {
            return new SalaryPeriod(
                currentMonthSalaryDate,
                CalendarRules.AddMonthsKeepingDay(currentMonthSalaryDate, 1, salaryDay));
        }

        var previousSalaryDate = CalendarRules.AddMonthsKeepingDay(currentMonthSalaryDate, -1, salaryDay);
        return new SalaryPeriod(previousSalaryDate, currentMonthSalaryDate);
    }

    public IReadOnlyList<SalaryPeriod> GetPeriods(DateOnly asOf, int salaryDay, int count)
    {
        if (count is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Dönem sayısı 1 ile 60 arasında olmalıdır.");
        }

        var first = GetPeriod(asOf, salaryDay);
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var start = CalendarRules.AddMonthsKeepingDay(first.Start, index, salaryDay);
                var end = CalendarRules.AddMonthsKeepingDay(first.Start, index + 1, salaryDay);
                return new SalaryPeriod(start, end);
            })
            .ToArray();
    }
}
