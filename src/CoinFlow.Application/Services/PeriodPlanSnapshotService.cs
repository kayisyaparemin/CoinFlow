using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class PeriodPlanSnapshotService(
    FinancialProjectionCalculator projectionCalculator,
    SalaryPeriodCalculator salaryPeriodCalculator,
    SalaryResolver salaryResolver)
{
    public PeriodPlanSnapshot Freeze(
        FinancialPlan financialPlan,
        FinancialSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        var reviewDate = salaryPeriodCalculator.GetNextReviewDate(
            snapshot.SnapshotDate,
            snapshot.SalaryDay);
        var projectionResult = projectionCalculator.CalculatePlan(
            financialPlan,
            snapshot.ProjectionAnchorDate,
            2);
        var projection = projectionResult.Periods[0];
        var planId = Guid.NewGuid();
        var lines = projectionResult.Periods
            .SelectMany(x => x.MandatoryItems)
            .Concat(projectionResult.FundingPlan.PreFirstSalaryObligations)
            .Where(item => item.Type != ObligationType.PlannedLargeExpense)
            .Where(item => IsInReviewWindow(
                item.DueDate,
                snapshot.SnapshotDate,
                reviewDate))
            .GroupBy(item => new
            {
                item.PaymentId,
                item.Type,
                item.DueDate
            })
            .Select(group => group.First())
            .Select(item => new PeriodPlanPaymentLine
            {
                PeriodPlanSnapshotId = planId,
                SourceEntityId = item.PaymentId,
                SourceType = Map(item.Type),
                Name = item.Name,
                PlannedDate = item.DueDate,
                PlannedAmount = item.Amount,
                IsEstimate = item.IsEstimate,
                Detail = item.IsPreFirstSalaryObligation
                    ? "İlk projection maaşından önce vadesi geliyordu."
                    : item.Detail
            })
            .ToList();

        foreach (var expense in financialPlan.PlannedLargeExpenses.Where(x =>
                     x.Status == PlannedExpenseStatus.Planned &&
                     IsInReviewWindow(
                         x.ExactDate,
                         snapshot.SnapshotDate,
                         reviewDate)))
        {
            lines.Add(new PeriodPlanPaymentLine
            {
                PeriodPlanSnapshotId = planId,
                SourceEntityId = expense.Id,
                SourceType = PlanPaymentSourceType.PlannedLargeExpense,
                Name = expense.Name,
                PlannedDate = expense.ExactDate,
                PlannedAmount = expense.Amount,
                Detail = expense.Note
            });
        }

        // Ödeme tercihi bilinmeyen kartlar da gerçek giriş formunda görünmelidir.
        foreach (var card in projectionResult.CardPaymentStatuses.Where(x =>
                     IsInReviewWindow(
                         x.PaymentDueDate,
                         snapshot.SnapshotDate,
                         reviewDate)))
        {
            if (lines.Any(x =>
                    x.SourceType == PlanPaymentSourceType.CreditCard &&
                    x.SourceEntityId == card.CardId &&
                    x.PlannedDate == card.PaymentDueDate))
            {
                continue;
            }

            lines.Add(new PeriodPlanPaymentLine
            {
                PeriodPlanSnapshotId = planId,
                SourceEntityId = card.CardId,
                SourceType = PlanPaymentSourceType.CreditCard,
                Name = card.CardName,
                PlannedDate = card.PaymentDueDate,
                PlannedAmount = card.Payment,
                IsEstimate = card.Resolution ==
                             CreditCardPaymentResolution.ProjectionFallback,
                Detail = card.Resolution ==
                         CreditCardPaymentResolution.Undetermined
                    ? "Ödeme tutarı dönem başında belirlenmemişti."
                    : string.Empty
            });
        }

        var orderedLines = lines
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .ToArray();
        decimal Sum(PlanPaymentSourceType type) => orderedLines
            .Where(x => x.SourceType == type)
            .Sum(x => x.PlannedAmount.GetValueOrDefault());
        var mandatory = orderedLines
            .Where(x => x.SourceType !=
                        PlanPaymentSourceType.PlannedLargeExpense)
            .Sum(x => x.PlannedAmount.GetValueOrDefault());
        var largeExpenses = Sum(PlanPaymentSourceType.PlannedLargeExpense);
        var salary = salaryResolver.Resolve(reviewDate, financialPlan.Salaries)
            ?.Amount ?? 0m;
        var otherIncome = financialPlan.OtherIncomes
            .Where(x => IsInReviewWindow(
                x.ExactDate,
                snapshot.SnapshotDate,
                reviewDate))
            .Sum(x => x.Amount);
        var plannedIncome = salary + otherIncome;
        var livingBudget = ResolveLivingBudget(
            financialPlan.Settings.MonthlyLivingBudget,
            snapshot.SnapshotDate,
            reviewDate,
            snapshot.SalaryDay);
        var endingBeforeDeficitInterest = snapshot.ProjectionStartingSavings +
                                          plannedIncome -
                                          mandatory -
                                          livingBudget -
                                          largeExpenses;
        var deficitInterest = endingBeforeDeficitInterest < 0m
            ? RoundMoney(
                Math.Abs(endingBeforeDeficitInterest) *
                financialPlan.Settings.DeficitFinancingInterestRate)
            : 0m;

        return new PeriodPlanSnapshot
        {
            Id = planId,
            FinancialSnapshotId = snapshot.Id,
            PeriodStart = snapshot.SnapshotDate,
            PeriodEnd = reviewDate,
            ReviewAvailableFrom = reviewDate,
            CreatedAtUtc = createdAtUtc,
            StrategyUsed = projection.PaymentAssignmentMode,
            PaymentWindowStart = snapshot.SnapshotDate.AddDays(1),
            PaymentWindowEnd = reviewDate,
            OpeningSavings = snapshot.ProjectionStartingSavings,
            PlannedIncome = plannedIncome,
            PlannedLoanPayments = Sum(PlanPaymentSourceType.Loan),
            PlannedCardPayments = Sum(PlanPaymentSourceType.CreditCard),
            PlannedTemporaryPayments = Sum(
                PlanPaymentSourceType.TemporaryPayment),
            PlannedInstallmentPayments = Sum(
                PlanPaymentSourceType.InstallmentPayment),
            PlannedOtherScheduledPayments = Sum(
                PlanPaymentSourceType.OtherScheduledPayment),
            PlannedMandatoryPayments = mandatory,
            PlannedLivingBudget = livingBudget,
            PlannedLargeExpenses = largeExpenses,
            PlannedCardInterest = projectionResult.CardPaymentStatuses
                .Where(x => IsInReviewWindow(
                    x.PaymentDueDate,
                    snapshot.SnapshotDate,
                    reviewDate))
                .Sum(x => x.CarryInterest),
            PlannedDeficitInterest = deficitInterest,
            PlannedEndingSavings = endingBeforeDeficitInterest -
                                   deficitInterest,
            PaymentLines = orderedLines
        };
    }

    private decimal ResolveLivingBudget(
        decimal monthlyLivingBudget,
        DateOnly snapshotDate,
        DateOnly reviewDate,
        int salaryDay)
    {
        var containingPeriod = salaryPeriodCalculator.GetPeriod(
            snapshotDate,
            salaryDay);
        if (snapshotDate == containingPeriod.Start)
        {
            return monthlyLivingBudget;
        }

        var reviewDayCount = reviewDate.DayNumber - snapshotDate.DayNumber;
        return RoundMoney(
            monthlyLivingBudget * reviewDayCount /
            containingPeriod.DayCount);
    }

    private static bool IsInReviewWindow(
        DateOnly activityDate,
        DateOnly snapshotDate,
        DateOnly reviewDate) =>
        activityDate > snapshotDate && activityDate <= reviewDate;

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static PlanPaymentSourceType Map(ObligationType type) =>
        type switch
        {
            ObligationType.Loan => PlanPaymentSourceType.Loan,
            ObligationType.CreditCard => PlanPaymentSourceType.CreditCard,
            ObligationType.TemporaryPayment =>
                PlanPaymentSourceType.TemporaryPayment,
            ObligationType.InstallmentPayment =>
                PlanPaymentSourceType.InstallmentPayment,
            ObligationType.OtherScheduledPayment =>
                PlanPaymentSourceType.OtherScheduledPayment,
            ObligationType.PlannedLargeExpense =>
                PlanPaymentSourceType.PlannedLargeExpense,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
}
