using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum IncomeSourceType
{
    Salary,
    OneTimeIncome
}

public sealed record IncomeProjectionItem(
    string Name,
    IncomeSourceType Type,
    DateOnly SourceDate,
    decimal Amount);

public sealed record IncomeProjectionSummary(
    IReadOnlyList<IncomeProjectionItem> Items,
    decimal SalaryIncome,
    decimal OtherIncome,
    decimal TotalIncome);

public sealed class IncomeProjectionCalculator(SalaryResolver salaryResolver)
{
    public IncomeProjectionSummary Calculate(
        SalaryPeriod period,
        IEnumerable<SalaryScheduleEntry> salaries,
        IEnumerable<OneTimeIncome> otherIncomes)
    {
        var items = new List<IncomeProjectionItem>();
        var salary = salaryResolver.Resolve(period.Start, salaries);
        if (salary is not null)
        {
            items.Add(new IncomeProjectionItem(
                string.IsNullOrWhiteSpace(salary.Description) ? "Maaş" : salary.Description,
                IncomeSourceType.Salary,
                period.Start,
                salary.Amount));
        }

        items.AddRange(otherIncomes
            .Where(x => period.Contains(x.ExactDate))
            .Select(x => new IncomeProjectionItem(
                string.IsNullOrWhiteSpace(x.Description) ? "Diğer gelir" : x.Description,
                IncomeSourceType.OneTimeIncome,
                x.ExactDate,
                x.Amount)));

        var ordered = items.OrderBy(x => x.SourceDate).ThenBy(x => x.Name).ToArray();
        var salaryTotal = ordered.Where(x => x.Type == IncomeSourceType.Salary).Sum(x => x.Amount);
        var otherTotal = ordered.Where(x => x.Type == IncomeSourceType.OneTimeIncome).Sum(x => x.Amount);
        return new IncomeProjectionSummary(ordered, salaryTotal, otherTotal, salaryTotal + otherTotal);
    }
}

