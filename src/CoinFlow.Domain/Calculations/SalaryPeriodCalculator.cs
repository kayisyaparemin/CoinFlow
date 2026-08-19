using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record SalaryPeriod(DateOnly Start, DateOnly End)
{
    public int DayCount => End.DayNumber - Start.DayNumber;
    public bool Contains(DateOnly date) => date >= Start && date < End;
}

public enum ObligationType
{
    Loan,
    TemporaryPayment,
    PlannedInstallment,
    CreditCard,
    EmergencyFund
}

public sealed record ObligationItem(
    string Name,
    ObligationType Type,
    DateOnly DueDate,
    decimal Amount,
    bool IsFinalPayment = false,
    bool IsEstimate = false);

public sealed record SalaryPeriodSummary(
    SalaryPeriod Period,
    decimal Salary,
    IReadOnlyList<ObligationItem> Obligations,
    decimal TotalObligations,
    decimal SpendableBudget,
    decimal DailyCoin);

public sealed class SalaryPeriodCalculator
{
    public SalaryPeriod GetPeriod(DateOnly date, int salaryDay)
    {
        CalendarRules.ValidateDay(salaryDay);
        var currentMonthSalary = CalendarRules.ResolveDay(date.Year, date.Month, salaryDay);

        if (date >= currentMonthSalary)
        {
            return new SalaryPeriod(
                currentMonthSalary,
                CalendarRules.AddMonthsKeepingDay(currentMonthSalary, 1, salaryDay));
        }

        var previous = CalendarRules.AddMonthsKeepingDay(currentMonthSalary, -1, salaryDay);
        return new SalaryPeriod(previous, currentMonthSalary);
    }

    public decimal ResolveSalary(DateOnly periodStart, IEnumerable<SalaryScheduleEntry> schedule)
    {
        return schedule
            .Where(x => x.EffectiveFrom <= periodStart)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => x.NetAmount)
            .FirstOrDefault();
    }

}
