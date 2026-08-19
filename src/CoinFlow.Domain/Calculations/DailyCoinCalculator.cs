using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record DailyCoinSnapshot(
    DateOnly AsOf,
    bool HasCurrentActual,
    SpendableBalanceSource BalanceSource,
    DateOnly OriginDate,
    decimal OriginAmount,
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
        decimal projectedPeriodBudget,
        SpendableBalanceState balance)
    {
        if (asOf < period.Start || asOf >= period.End)
        {
            throw new ArgumentOutOfRangeException(nameof(asOf), "Tarih maaş döneminin içinde olmalıdır.");
        }

        var totalDays = period.DayCount;
        var remainingDays = period.End.DayNumber - asOf.DayNumber;
        if (balance.RequiresSnapshot)
        {
            return new DailyCoinSnapshot(
                asOf,
                false,
                balance.Source,
                balance.OriginDate,
                0m,
                projectedPeriodBudget,
                0m,
                0m,
                totalDays,
                0,
                remainingDays,
                0m,
                0m,
                0m,
                0m,
                0m,
                totalDays == 0 ? 1m : decimal.Round((decimal)(asOf.DayNumber - period.Start.DayNumber) / totalDays, 4));
        }

        var rewardDays = period.End.DayNumber - balance.OriginDate.DayNumber;
        var elapsedDays = asOf.DayNumber - balance.OriginDate.DayNumber + 1;
        var dailyReward = rewardDays <= 0
            ? 0m
            : decimal.Round(balance.OriginAmount / rewardDays, 2, MidpointRounding.AwayFromZero);
        var pool = (dailyReward * elapsedDays) - balance.EligibleExpenses;
        var sustainable = remainingDays == 0
            ? balance.CurrentAvailable
            : decimal.Round(balance.CurrentAvailable / remainingDays, 2, MidpointRounding.AwayFromZero);
        var progress = rewardDays <= 0 ? 1m : decimal.Round((decimal)(elapsedDays - 1) / rewardDays, 4);

        return new DailyCoinSnapshot(
            asOf,
            true,
            balance.Source,
            balance.OriginDate,
            balance.OriginAmount,
            projectedPeriodBudget,
            balance.EligibleExpenses,
            balance.CurrentAvailable,
            totalDays,
            elapsedDays,
            remainingDays,
            dailyReward,
            balance.TodayEligibleExpenses,
            dailyReward - balance.TodayEligibleExpenses,
            pool,
            sustainable,
            progress);
    }

    public DailyCoinSnapshot Calculate(
        SalaryPeriod period,
        DateOnly asOf,
        decimal periodBudget,
        IEnumerable<Expense> expenses)
    {
        var eligible = expenses
            .Where(x => x.Date >= period.Start && x.Date <= asOf)
            .Where(SpendableBalanceCalculator.AffectsCurrentBalance)
            .ToArray();
        var spent = eligible.Sum(x => x.Amount);
        var state = new SpendableBalanceState(
            SpendableBalanceSource.PeriodStart,
            false,
            period.Start,
            periodBudget,
            spent,
            eligible.Where(x => x.Date == asOf).Sum(x => x.Amount),
            periodBudget - spent,
            null);
        return Calculate(period, asOf, periodBudget, state);
    }

    public static bool AffectsCashBudget(Expense expense) =>
        SpendableBalanceCalculator.AffectsCurrentBalance(expense);
}
