using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class FinancialProjectionCalculator(
    SalaryPeriodCalculator salaryPeriodCalculator,
    IncomeProjectionCalculator incomeProjectionCalculator,
    CreditCardStatementCalculator cardStatementCalculator,
    MandatoryPaymentCalculator mandatoryPaymentCalculator,
    SalaryFundingPlanner fundingPlanner,
    PaymentAssignmentStrategyResolver strategyResolver)
{
    public IReadOnlyList<SalaryPeriodProjection> Calculate(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount = 12) =>
        CalculatePlan(plan, asOf, periodCount).Periods;

    public FinancialProjectionResult CalculatePlan(
        FinancialPlan plan,
        DateOnly asOf,
        int periodCount = 12)
    {
        Validate(plan);
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? asOf
            : plan.Settings.ProjectionAnchorDate;
        var firstSalary = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
            anchor,
            plan.Settings.SalaryDay);
        var periods = BuildPeriods(
            firstSalary,
            plan.Settings.SalaryDay,
            periodCount);
        strategyResolver.ValidateHistory(
            plan.PaymentAssignmentStrategies,
            plan.Settings.SalaryDay,
            firstSalary);

        var cardBundle = BuildCardPayments(
            plan.CreditCards,
            periods[^1].End,
            plan.Settings.CreditCardCarryInterestRate);
        var obligations = mandatoryPaymentCalculator
            .BuildObligations(
                plan.Loans,
                plan.PaymentPlans,
                cardBundle.Obligations)
            .Concat(BuildLargeExpenseObligations(plan.PlannedLargeExpenses))
            .ToArray();
        var fundingPlan = fundingPlanner.Plan(
            periods,
            obligations,
            anchor,
            plan.Settings.SalaryDay,
            plan.PaymentAssignmentStrategies);
        var statuses = AssignCardStatuses(cardBundle.Statuses, fundingPlan);
        var result = new List<SalaryPeriodProjection>(periods.Count);
        var openingSavings = plan.Settings.ProjectionStartingSavings;

        foreach (var period in periods)
        {
            var budget = fundingPlan.Budgets.Single(x =>
                x.SalaryDate == period.Start);
            var income = incomeProjectionCalculator.Calculate(
                period,
                plan.Salaries,
                plan.OtherIncomes);
            var mandatory = mandatoryPaymentCalculator.Summarize(budget.Items);
            var availableAfterMandatory = income.TotalIncome - mandatory.Total;
            var largeExpenses = budget.Items
                .Where(x => x.Type == ObligationType.PlannedLargeExpense)
                .Select(item => plan.PlannedLargeExpenses.Single(x =>
                    x.Id == item.PaymentId))
                .OrderBy(x => x.ExactDate)
                .ThenBy(x => x.Name)
                .ToArray();
            var largeExpenseTotal = largeExpenses.Sum(x => x.Amount);
            var estimatedSavings = availableAfterMandatory -
                                   plan.Settings.MonthlyLivingBudget -
                                   largeExpenseTotal;
            var periodStatuses = statuses
                .Where(x => x.AssignedSalaryDate == period.Start)
                .ToArray();
            var cardInterest = periodStatuses.Sum(x => x.CarryInterest);
            var endingBeforeDeficitInterest = openingSavings + estimatedSavings;
            var deficitInterest = endingBeforeDeficitInterest < 0m
                ? RoundMoney(
                    Math.Abs(endingBeforeDeficitInterest) *
                    plan.Settings.DeficitFinancingInterestRate)
                : 0m;
            var endingSavings = endingBeforeDeficitInterest - deficitInterest;

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
                periodStatuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.ProjectionFallback),
                periodStatuses.Any(x =>
                    x.Resolution == CreditCardPaymentResolution.Undetermined),
                availableAfterMandatory < 0m ||
                estimatedSavings < 0m ||
                endingSavings < 0m,
                income.Items,
                mandatory.Items,
                largeExpenses,
                periodStatuses,
                budget.Mode,
                budget.CoverageStart,
                budget.CoverageEnd,
                budget.IsStrategyTransition,
                budget.IsInitialSnapshotPeriod,
                budget.NormalMandatoryAmount,
                budget.TransitionCatchUpAmount,
                budget.ForwardFundedAmount,
                anchor,
                endingBeforeDeficitInterest,
                deficitInterest,
                cardInterest,
                plan.Settings.DeficitFinancingInterestRate));
            openingSavings = endingSavings;
        }

        return new FinancialProjectionResult(result, fundingPlan);
    }

    private static IReadOnlyList<SalaryPeriod> BuildPeriods(
        DateOnly firstSalary,
        int salaryDay,
        int count)
    {
        if (count is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "Dönem sayısı 1 ile 60 arasında olmalıdır.");
        }

        return Enumerable.Range(0, count)
            .Select(index => new SalaryPeriod(
                CalendarRules.AddMonthsKeepingDay(
                    firstSalary, index, salaryDay),
                CalendarRules.AddMonthsKeepingDay(
                    firstSalary, index + 1, salaryDay)))
            .ToArray();
    }

    private CardPaymentBundle BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly horizonEnd,
        decimal carryInterestRate)
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
                         .Project(
                             card,
                             statementCount,
                             useProjectionFallback: true,
                             carryInterestRate)
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
                    statement.OpeningCarriedBalance,
                    statement.NewCharges,
                    statement.CarriedAfterPayment,
                    statement.CarryInterest,
                    statement.NextCarriedBalance,
                    statement.AppliedInterestRate,
                    statement.PaymentResolution,
                    statement.AppliedPaymentType));

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
                        },
                        PaymentId: card.Id));
                }
            }
        }

        return new CardPaymentBundle(obligations, statuses);
    }

    private static IEnumerable<ObligationItem> BuildLargeExpenseObligations(
        IEnumerable<PlannedLargeExpense> expenses) => expenses
        .Where(x => x.Status == PlannedExpenseStatus.Planned)
        .Select(x => new ObligationItem(
            x.Name,
            ObligationType.PlannedLargeExpense,
            x.ExactDate,
            x.Amount,
            Detail: x.Note,
            PaymentId: x.Id));

    private static IReadOnlyList<CreditCardPaymentProjectionStatus>
        AssignCardStatuses(
            IEnumerable<CreditCardPaymentProjectionStatus> statuses,
            SalaryFundingPlan fundingPlan) => statuses
        .Select(status =>
        {
            var budget = fundingPlan.FindBudget(status.PaymentDueDate);
            if (budget is not null)
            {
                return status with
                {
                    AssignedSalaryDate = budget.SalaryDate,
                    PaymentBeforeSalary =
                        status.PaymentDueDate < budget.SalaryDate,
                    ActiveMode = budget.Mode,
                    AssignmentReason = budget.ResolveReason(
                        status.PaymentDueDate)
                };
            }

            var isPreFirst = fundingPlan.IsPreFirstSalaryDate(
                status.PaymentDueDate);
            return status with
            {
                ActiveMode = isPreFirst
                    ? fundingPlan.Budgets[0].Mode
                    : null,
                AssignmentReason = isPreFirst
                    ? PaymentAssignmentReason.PreFirstSalaryUpcoming
                    : null,
                IsPreFirstSalaryObligation = isPreFirst
            };
        })
        .ToArray();

    private static void Validate(FinancialPlan plan)
    {
        CalendarRules.ValidateDay(plan.Settings.SalaryDay);
        if (plan.Settings.MonthlyLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Aylık tahmini yaşam bütçesi negatif olamaz.");
        }

        ValidateInterestRate(
            plan.Settings.CreditCardCarryInterestRate,
            "Kredi kartı devreden borç faiz oranı");
        ValidateInterestRate(
            plan.Settings.DeficitFinancingInterestRate,
            "Finansman açığı faiz oranı");

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

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void ValidateInterestRate(decimal rate, string name)
    {
        if (rate is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                $"{name} %0 ile %100 arasında olmalıdır.");
        }
    }

    private sealed record CardPaymentBundle(
        IReadOnlyList<ObligationItem> Obligations,
        IReadOnlyList<CreditCardPaymentProjectionStatus> Statuses);
}
