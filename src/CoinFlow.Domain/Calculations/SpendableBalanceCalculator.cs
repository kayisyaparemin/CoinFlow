using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum SpendableBalanceSource
{
    Missing = 0,
    PeriodStart = 1,
    Snapshot = 2
}

public sealed record SpendableBalanceState(
    SpendableBalanceSource Source,
    bool RequiresSnapshot,
    DateOnly OriginDate,
    decimal OriginAmount,
    decimal EligibleExpenses,
    decimal TodayEligibleExpenses,
    decimal CurrentAvailable,
    SpendableBalanceSnapshot? Snapshot);

public sealed class SpendableBalanceCalculator
{
    public SpendableBalanceState Calculate(
        SalaryPeriod period,
        DateOnly asOf,
        decimal projectedPeriodBudget,
        DateOnly? trackingStartedDate,
        IEnumerable<SpendableBalanceSnapshot> snapshots,
        IEnumerable<Expense> expenses)
    {
        if (!period.Contains(asOf))
        {
            throw new ArgumentOutOfRangeException(nameof(asOf), "Tarih maaş döneminin içinde olmalıdır.");
        }

        var latest = snapshots
            .Where(x => x.SalaryPeriodStart == period.Start)
            .Where(x => x.SnapshotDate >= period.Start && x.SnapshotDate <= asOf)
            .OrderByDescending(x => x.SnapshotDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        if (latest is not null)
        {
            var eligible = EligibleExpenses(expenses, latest, asOf).ToArray();
            var spent = eligible.Sum(x => x.Amount);
            return new SpendableBalanceState(
                SpendableBalanceSource.Snapshot,
                false,
                latest.SnapshotDate,
                latest.Amount,
                spent,
                eligible.Where(x => x.Date == asOf).Sum(x => x.Amount),
                latest.Amount - spent,
                latest);
        }

        if (trackingStartedDate is not null && trackingStartedDate <= period.Start)
        {
            var eligible = expenses
                .Where(x => x.Date >= period.Start && x.Date <= asOf)
                .Where(AffectsCurrentBalance)
                .ToArray();
            var spent = eligible.Sum(x => x.Amount);
            return new SpendableBalanceState(
                SpendableBalanceSource.PeriodStart,
                false,
                period.Start,
                projectedPeriodBudget,
                spent,
                eligible.Where(x => x.Date == asOf).Sum(x => x.Amount),
                projectedPeriodBudget - spent,
                null);
        }

        return new SpendableBalanceState(
            SpendableBalanceSource.Missing,
            true,
            asOf,
            0m,
            0m,
            0m,
            0m,
            null);
    }

    public static bool AffectsCurrentBalance(Expense expense) =>
        expense.PaymentType is ExpensePaymentType.Cash or ExpensePaymentType.Other;

    private static IEnumerable<Expense> EligibleExpenses(
        IEnumerable<Expense> expenses,
        SpendableBalanceSnapshot snapshot,
        DateOnly asOf) => expenses
        .Where(AffectsCurrentBalance)
        .Where(x => x.Date <= asOf)
        .Where(x => x.Date > snapshot.SnapshotDate ||
                    x.Date == snapshot.SnapshotDate && x.CreatedAtUtc > snapshot.CreatedAtUtc);
}
