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
        var nextCard = NextCardPayment(data.CreditCards, asOf);
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
            nextCard?.StatementBalance,
            nextCard?.MinimumPayment,
            nextCard?.PlannedPayment,
            nextCard?.Resolution);

        return new DashboardSnapshot(
            salarySummary,
            daily,
            next,
            data.EmergencyFund,
            data.Settings.GamificationEnabled,
            CreateEncouragement(daily, data.Settings.GamificationEnabled),
            details,
            nextCard);
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
        var cardProjection = BuildCardPayments(data.CreditCards, periods[^1].End);
        var rows = new List<FutureMonthProjection>(periods.Length);

        for (var index = 0; index < periods.Length; index++)
        {
            var period = periods[index];
            var salary = salaryPeriodCalculator.ResolveSalary(period.Start, data.Salaries);
            var mandatory = mandatoryPaymentCalculator.Calculate(
                period,
                data.Loans,
                data.PaymentPlans,
                cardProjection.Obligations,
                contributions[index]);
            var projectedSpendable = salary - mandatory.ProjectedTotal;
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

            var cardStatuses = cardProjection.Statuses
                .Where(x => period.Contains(x.PaymentDueDate))
                .ToArray();
            rows.Add(new FutureMonthProjection(
                period,
                salary,
                mandatory.LoanPayments,
                mandatory.CreditCardPayments,
                mandatory.TemporaryPayments,
                mandatory.PlannedInstallments,
                mandatory.EmergencyFundContribution,
                mandatory.ProjectedTotal,
                projectedSpendable,
                actual,
                projectedDaily,
                CreateHighlights(period, data, cardStatuses),
                cardStatuses));
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
            cardPayments.Obligations,
            emergencyContribution);
    }

    private CardPaymentProjectionBundle BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly horizonEnd)
    {
        var obligations = new List<ObligationItem>();
        var statuses = new List<CreditCardPaymentProjectionStatus>();
        foreach (var card in cards)
        {
            var firstClose = CreditCardProjectionCalculator.ResolveStatementCloseOnOrAfter(
                card.BalanceAsOfDate,
                card.StatementClosingDay);
            var months = Math.Max(2, MonthDistance(firstClose, horizonEnd) + 3);
            var cardName = $"{card.Bank} {card.Name}".Trim();
            foreach (var statement in cardCalculator
                         .Project(card, months, useProjectionFallback: true)
                         .Where(x => x.PaymentDueDate < horizonEnd))
            {
                statuses.Add(new CreditCardPaymentProjectionStatus(
                    card.Id,
                    cardName,
                    statement.StatementCloseDate,
                    statement.PaymentDueDate,
                    statement.StatementBalance,
                    statement.MinimumPayment,
                    statement.Payment,
                    statement.PaymentResolution,
                    statement.AppliedPaymentType));
                if (statement.Payment is decimal payment)
                {
                    obligations.Add(new ObligationItem(
                        cardName,
                        ObligationType.CreditCard,
                        statement.PaymentDueDate,
                        payment,
                        IsEstimate: statement.PaymentResolution == CreditCardPaymentResolution.ProjectionFallback));
                }
            }
        }

        return new CardPaymentProjectionBundle(obligations, statuses);
    }

    private UpcomingCardPayment? NextCardPayment(
        IEnumerable<CreditCard> cards,
        DateOnly asOf) => cards
            .SelectMany(card =>
            {
                var firstClose = CreditCardProjectionCalculator.ResolveStatementCloseOnOrAfter(
                    card.BalanceAsOfDate,
                    card.StatementClosingDay);
                var cardName = $"{card.Bank} {card.Name}".Trim();
                return cardCalculator
                    .Project(card, Math.Max(24, MonthDistance(firstClose, asOf) + 24), useProjectionFallback: true)
                    .Select(statement => new UpcomingCardPayment(
                        card.Id,
                        cardName,
                        statement.StatementCloseDate,
                        statement.PaymentDueDate,
                        statement.StatementBalance,
                        statement.MinimumPayment,
                        statement.Payment,
                        statement.PaymentResolution,
                        statement.AppliedPaymentType));
            })
            .Where(x => x.PaymentDueDate >= asOf)
            .OrderBy(x => x.PaymentDueDate)
            .FirstOrDefault();

    private IReadOnlyList<string> CreateHighlights(
        SalaryPeriod period,
        FinanceData data,
        IReadOnlyList<CreditCardPaymentProjectionStatus> cardStatuses)
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
        result.AddRange(cardStatuses
            .Where(x => x.Resolution == CreditCardPaymentResolution.Undetermined)
            .Select(x => $"{x.CardName} kart ödemesi henüz seçilmedi; dönem bütçesi kesin değil."));
        result.AddRange(cardStatuses
            .Where(x => x.Resolution == CreditCardPaymentResolution.ProjectionFallback)
            .Select(x => $"Tahmin: {x.CardName} için {FallbackLabel(x.PaymentType)} varsayıldı."));
        return result;
    }

    private static string FallbackLabel(CreditCardPaymentType? paymentType) => paymentType switch
    {
        CreditCardPaymentType.Minimum => "asgari ödeme",
        CreditCardPaymentType.FullStatement => "ekstre tamamı",
        CreditCardPaymentType.FixedAmount => "sabit ödeme",
        _ => "geçici kart ödemesi"
    };

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

    private sealed record CardPaymentProjectionBundle(
        IReadOnlyList<ObligationItem> Obligations,
        IReadOnlyList<CreditCardPaymentProjectionStatus> Statuses);
}
