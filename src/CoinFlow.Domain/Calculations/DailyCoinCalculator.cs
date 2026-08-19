using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record DailyCoinSnapshot(
    DateOnly AsOf,
    decimal PeriodBudget,
    decimal PeriodCashSpending,
    decimal RemainingBudget,
    int TotalPeriodDays,
    int ElapsedDays,
    int RemainingDays,
    decimal BaseDailyCoin,
    decimal TodaySpending,
    decimal TodayEarned,
    decimal CoinPool,
    decimal SustainableDailyBudget,
    decimal ProgressRate);

public sealed class DailyCoinCalculator
{
    public DailyCoinSnapshot Calculate(
        SalaryPeriod period,
        DateOnly asOf,
        decimal periodBudget,
        IEnumerable<Expense> expenses)
    {
        if (asOf < period.Start || asOf >= period.End)
        {
            throw new ArgumentOutOfRangeException(nameof(asOf), "Tarih maaş döneminin içinde olmalıdır.");
        }

        var cashExpenses = expenses
            .Where(x => x.Date >= period.Start && x.Date <= asOf)
            .Where(AffectsCashBudget)
            .ToArray();

        var totalDays = period.DayCount;
        var elapsedDays = asOf.DayNumber - period.Start.DayNumber + 1;
        var remainingDays = period.End.DayNumber - asOf.DayNumber;
        var baseDaily = totalDays == 0
            ? 0m
            : decimal.Round(periodBudget / totalDays, 2, MidpointRounding.AwayFromZero);
        var spent = cashExpenses.Sum(x => x.Amount);
        var todaySpent = cashExpenses.Where(x => x.Date == asOf).Sum(x => x.Amount);
        var remaining = periodBudget - spent;
        var pool = (baseDaily * elapsedDays) - spent;
        var sustainable = remainingDays == 0
            ? remaining
            : decimal.Round(remaining / remainingDays, 2, MidpointRounding.AwayFromZero);
        var progress = totalDays == 0 ? 1m : decimal.Round((decimal)(elapsedDays - 1) / totalDays, 4);

        return new DailyCoinSnapshot(
            asOf,
            periodBudget,
            spent,
            remaining,
            totalDays,
            elapsedDays,
            remainingDays,
            baseDaily,
            todaySpent,
            baseDaily - todaySpent,
            pool,
            sustainable,
            progress);
    }

    public static bool AffectsCashBudget(Expense expense) =>
        expense.PaymentType is ExpensePaymentType.Cash or ExpensePaymentType.Other;
}
