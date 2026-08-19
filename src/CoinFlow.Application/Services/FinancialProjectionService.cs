using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class FinancialProjectionService(
    SalaryPeriodCalculator salaryPeriodCalculator,
    LoanScheduleCalculator loanScheduleCalculator,
    CreditCardProjectionCalculator cardCalculator,
    MandatoryPaymentCalculator mandatoryPaymentCalculator,
    SpendableBalanceCalculator spendableBalanceCalculator,
    DailyCoinCalculator dailyCoinCalculator,
    EmergencyFundCalculator emergencyFundCalculator)
{
    public DashboardSnapshot BuildDashboard(FinanceData data, DateOnly asOf)
    {
        var periods = BuildFuturePeriods(data, asOf, 2);
        var current = periods[0];
        var currentMandatory = BuildMandatoryForPeriod(data, current.Period, current.EmergencyFundContribution);
        var salarySummary = new SalaryPeriodSummary(
            current.Period,
            current.Salary,
            currentMandatory.Items,
            current.TotalObligations,
            current.ProjectedSpendable,
            current.ProjectedDailyCoin);
        var balance = spendableBalanceCalculator.Calculate(
            current.Period,
            asOf,
            current.ProjectedSpendable,
            data.Settings.TrackingStartedDate,
            data.SpendableBalanceSnapshots,
            data.Expenses);
        var daily = dailyCoinCalculator.Calculate(current.Period, asOf, current.ProjectedSpendable, balance);
        var next = periods.Count > 1 ? periods[1] : current;
        var nextCard = NextCardStatement(data.CreditCards, asOf);
        var details = new CalculationDetails(
            current.Period,
            balance.Source,
            balance.Snapshot?.SnapshotDate,
            balance.OriginAmount,
            balance.EligibleExpenses,
            balance.CurrentAvailable,
            daily.RemainingDays,
            daily.SustainableDailyBudget,
            nextCard?.StatementCloseDate,
            nextCard?.PaymentDueDate,
            nextCard?.StatementBalance ?? 0m,
            nextCard?.Payment ?? 0m);

        return new DashboardSnapshot(
            salarySummary,
            daily,
            next,
            data.EmergencyFund,
            data.Settings.GamificationEnabled,
            CreateEncouragement(daily, data.Settings.GamificationEnabled),
            details);
    }

    public IReadOnlyList<FutureMonthProjection> BuildFuturePeriods(
        FinanceData data,
        DateOnly asOf,
        int periodCount)
    {
        if (periodCount is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(periodCount));
        }

        var first = salaryPeriodCalculator.GetPeriod(asOf, data.Settings.SalaryDay);
        var periods = Enumerable.Range(0, periodCount)
            .Select(index =>
            {
                var start = CalendarRules.AddMonthsKeepingDay(first.Start, index, data.Settings.SalaryDay);
                return new SalaryPeriod(
                    start,
                    CalendarRules.AddMonthsKeepingDay(start, 1, data.Settings.SalaryDay));
            })
            .ToArray();
        var contributions = emergencyFundCalculator.ProjectContributions(
            data.EmergencyFund,
            periods,
            data.EmergencyFundTransfers);
        var cardPayments = BuildCardPayments(data.CreditCards, periods[^1].End);
        var rows = new List<FutureMonthProjection>(periods.Length);

        for (var index = 0; index < periods.Length; index++)
        {
            var period = periods[index];
            var salary = salaryPeriodCalculator.ResolveSalary(period.Start, data.Salaries);
            var mandatory = mandatoryPaymentCalculator.Calculate(
                period,
                data.Loans,
                data.PaymentPlans,
                cardPayments,
                contributions[index]);
            var projectedSpendable = salary - mandatory.Total;
            var projectedDaily = period.DayCount == 0
                ? 0m
                : decimal.Round(projectedSpendable / period.DayCount, 2, MidpointRounding.AwayFromZero);
            decimal? actual = null;
            if (index == 0)
            {
                var balance = spendableBalanceCalculator.Calculate(
                    period,
                    asOf,
                    projectedSpendable,
                    data.Settings.TrackingStartedDate,
                    data.SpendableBalanceSnapshots,
                    data.Expenses);
                if (!balance.RequiresSnapshot)
                {
                    actual = balance.CurrentAvailable;
                }
            }

            rows.Add(new FutureMonthProjection(
                period,
                salary,
                mandatory.LoanPayments,
                mandatory.CreditCardPayments,
                mandatory.TemporaryPayments,
                mandatory.PlannedInstallments,
                mandatory.EmergencyFundContribution,
                mandatory.Total,
                projectedSpendable,
                actual,
                projectedDaily,
                CreateHighlights(period, data)));
        }

        return rows;
    }

    private MandatoryPaymentSummary BuildMandatoryForPeriod(
        FinanceData data,
        SalaryPeriod period,
        decimal emergencyContribution)
    {
        var cardPayments = BuildCardPayments(data.CreditCards, period.End.AddMonths(1));
        return mandatoryPaymentCalculator.Calculate(
            period,
            data.Loans,
            data.PaymentPlans,
            cardPayments,
            emergencyContribution);
    }

    private IReadOnlyList<ObligationItem> BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly horizonEnd)
    {
        var results = new List<ObligationItem>();
        foreach (var card in cards)
        {
            var firstClose = CreditCardProjectionCalculator.ResolveStatementCloseOnOrAfter(
                card.BalanceAsOfDate,
                card.StatementClosingDay);
            var months = Math.Max(2, MonthDistance(firstClose, horizonEnd) + 3);
            results.AddRange(cardCalculator
                .Project(card, months)
                .Where(x => x.PaymentDueDate < horizonEnd)
                .Select(x => new ObligationItem(
                    $"{card.Bank} {card.Name}".Trim(),
                    ObligationType.CreditCard,
                    x.PaymentDueDate,
                    x.Payment)));
        }

        return results;
    }

    private CreditCardStatementProjection? NextCardStatement(
        IEnumerable<CreditCard> cards,
        DateOnly asOf) => cards
        .SelectMany(card =>
        {
            var firstClose = CreditCardProjectionCalculator.ResolveStatementCloseOnOrAfter(
                card.BalanceAsOfDate,
                card.StatementClosingDay);
            return cardCalculator.Project(card, Math.Max(24, MonthDistance(firstClose, asOf) + 24));
        })
        .Where(x => x.PaymentDueDate >= asOf)
        .OrderBy(x => x.PaymentDueDate)
        .FirstOrDefault();

    private IReadOnlyList<string> CreateHighlights(SalaryPeriod period, FinanceData data)
    {
        var result = data.Loans
            .Where(x => loanScheduleCalculator.GetPaymentDates(x).LastOrDefault() is var last &&
                        last != default && period.Contains(last))
            .Select(x => $"{x.Name}: bu dönem son ödeme!")
            .ToList();
        result.AddRange(data.PaymentPlans
            .Where(x => x.Installments.Where(i => !i.IsPaid).OrderBy(i => i.DueDate).LastOrDefault() is var last &&
                        last is not null && period.Contains(last.DueDate))
            .Select(x => $"{x.Name}: bu dönem tamamlanıyor."));
        return result;
    }

    private static string CreateEncouragement(DailyCoinSnapshot daily, bool gamified)
    {
        if (!daily.HasCurrentActual)
        {
            return "Gerçek günlük bütçeni görmek için şu anki serbest bakiyeni gir.";
        }

        if (daily.TodayEarned >= 0m)
        {
            return gamified
                ? $"Bugün {daily.TodayEarned:N0} coin farm'ladın."
                : $"Bugünkü Daily Reward'dan {daily.TodayEarned:N0} TL kaldı.";
        }

        return gamified
            ? $"Bugün coin havuzundan {Math.Abs(daily.TodayEarned):N0} TL kullandın."
            : $"Bugünkü harcama Daily Reward'ın {Math.Abs(daily.TodayEarned):N0} TL üzerinde.";
    }

    private static int MonthDistance(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;
}
