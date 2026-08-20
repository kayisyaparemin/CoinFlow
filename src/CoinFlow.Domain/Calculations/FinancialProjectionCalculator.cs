using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class FinancialProjectionCalculator(
    SalaryPeriodCalculator salaryPeriodCalculator,
    IncomeProjectionCalculator incomeProjectionCalculator,
    CreditCardStatementCalculator cardStatementCalculator,
    MandatoryPaymentCalculator mandatoryPaymentCalculator,
    PaymentAssignmentResolver assignmentResolver)
{
    public IReadOnlyList<SalaryPeriodProjection> Calculate(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount = 12,
        PaymentAssignmentMode? assignmentModeOverride = null)
    {
        Validate(plan);
        var assignmentMode = assignmentModeOverride ??
                             plan.Settings.PaymentAssignmentMode;
        var periods = salaryPeriodCalculator.GetPeriods(
            asOf,
            plan.Settings.SalaryDay,
            periodCount);
        var cardBundle = BuildCardPayments(
            plan.CreditCards,
            periods[^1].End,
            plan.Settings.SalaryDay,
            assignmentMode);
        var result = new List<SalaryPeriodProjection>(periods.Count);
        var openingSavings = plan.Settings.ProjectionStartingSavings;

        foreach (var period in periods)
        {
            var income = incomeProjectionCalculator.Calculate(
                period,
                plan.Salaries,
                plan.OtherIncomes);
            var mandatory = mandatoryPaymentCalculator.Calculate(
                period,
                plan.Loans,
                plan.PaymentPlans,
                cardBundle.Obligations,
                plan.Settings.SalaryDay,
                assignmentMode);
            var availableAfterMandatory = income.TotalIncome - mandatory.Total;
            var estimatedSavings = availableAfterMandatory -
                                   plan.Settings.MonthlyLivingBudget;
            var largeExpenses = plan.PlannedLargeExpenses
                .Where(x => x.Status == PlannedExpenseStatus.Planned)
                .Where(x => assignmentResolver.ResolveFundingSalaryDate(
                    x.ExactDate,
                    plan.Settings.SalaryDay,
                    assignmentMode) == period.Start)
                .OrderBy(x => x.ExactDate)
                .ThenBy(x => x.Name)
                .ToArray();
            var largeExpenseTotal = largeExpenses.Sum(x => x.Amount);
            var endingSavings = openingSavings + estimatedSavings - largeExpenseTotal;
            var statuses = cardBundle.Statuses
                .Where(x => x.AssignedSalaryDate == period.Start)
                .ToArray();
            var paymentWindow = assignmentResolver.ResolveWindow(
                period.Start,
                plan.Settings.SalaryDay,
                assignmentMode);

            result.Add(new SalaryPeriodProjection(
                period.Start,
                period.End,
                income.SalaryIncome,
                income.OtherIncome,
                income.TotalIncome,
                mandatory.LoanPayments,
                mandatory.CreditCardPayments,
                mandatory.TemporaryPayments,
                mandatory.InstallmentPayments,
                mandatory.OtherScheduledPayments,
                mandatory.Total,
                availableAfterMandatory,
                plan.Settings.MonthlyLivingBudget,
                estimatedSavings,
                largeExpenseTotal,
                openingSavings,
                endingSavings,
                statuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.ProjectionFallback),
                statuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.Undetermined),
                availableAfterMandatory < 0m || estimatedSavings < 0m,
                income.Items,
                mandatory.Items,
                largeExpenses,
                statuses,
                assignmentMode,
                paymentWindow.StartInclusive,
                paymentWindow.EndInclusive));

            openingSavings = endingSavings;
        }

        return result;
    }

    private CardPaymentBundle BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly horizonEnd,
        int salaryDay,
        PaymentAssignmentMode assignmentMode)
    {
        var obligations = new List<ObligationItem>();
        var statuses = new List<CreditCardPaymentProjectionStatus>();

        foreach (var card in cards)
        {
            var firstClose = CreditCardStatementCalculator
                .ResolveStatementCloseOnOrAfter(
                    card.BalanceAsOfDate,
                    card.StatementClosingDay);
            var statementCount = Math.Max(
                2,
                MonthDistance(firstClose, horizonEnd) + 3);
            var cardName = $"{card.Bank} {card.Name}".Trim();

            foreach (var statement in cardStatementCalculator
                         .Project(card, statementCount, useProjectionFallback: true)
                         .Where(x => x.PaymentDueDate < horizonEnd))
            {
                var assignedSalaryDate = assignmentResolver
                    .ResolveFundingSalaryDate(
                        statement.PaymentDueDate,
                        salaryDay,
                        assignmentMode);
                statuses.Add(new CreditCardPaymentProjectionStatus(
                    card.Id,
                    cardName,
                    statement.StatementCloseDate,
                    statement.PaymentDueDate,
                    statement.StatementBalance,
                    statement.MinimumPayment,
                    statement.Payment,
                    statement.PaymentResolution,
                    statement.AppliedPaymentType,
                    assignedSalaryDate,
                    statement.PaymentDueDate < assignedSalaryDate));

                if (statement.Payment is decimal payment)
                {
                    obligations.Add(new ObligationItem(
                        cardName,
                        ObligationType.CreditCard,
                        statement.PaymentDueDate,
                        payment,
                        IsEstimate: statement.PaymentResolution ==
                                    CreditCardPaymentResolution.ProjectionFallback,
                        Detail: statement.PaymentResolution switch
                        {
                            CreditCardPaymentResolution.ProjectionFallback =>
                                "Projeksiyon varsayımı",
                            CreditCardPaymentResolution.DueDateOverride =>
                                "Due-date ödeme planı",
                            _ => "Kart ödeme stratejisi"
                        }));
                }
            }
        }

        return new CardPaymentBundle(obligations, statuses);
    }

    private static void Validate(FinancialPlan plan)
    {
        CalendarRules.ValidateDay(plan.Settings.SalaryDay);
        if (!Enum.IsDefined(plan.Settings.PaymentAssignmentMode))
        {
            throw new InvalidOperationException(
                "Maaş kullanım şekli geçersiz.");
        }
        if (plan.Settings.MonthlyLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Aylık tahmini yaşam bütçesi negatif olamaz.");
        }

        if (plan.Salaries.Any(x => x.Amount < 0m) ||
            plan.OtherIncomes.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException("Gelir tutarı negatif olamaz.");
        }

        if (plan.PlannedLargeExpenses.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException(
                "Planlanan büyük harcama negatif olamaz.");
        }
    }

    private static int MonthDistance(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;

    private sealed record CardPaymentBundle(
        IReadOnlyList<ObligationItem> Obligations,
        IReadOnlyList<CreditCardPaymentProjectionStatus> Statuses);
}
