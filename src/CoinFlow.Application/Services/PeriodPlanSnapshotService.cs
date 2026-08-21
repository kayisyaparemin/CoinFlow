using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class PeriodPlanSnapshotService(
    FinancialProjectionCalculator projectionCalculator)
{
    public PeriodPlanSnapshot Freeze(
        FinancialPlan financialPlan,
        FinancialSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        var projectionResult = projectionCalculator.CalculatePlan(
            financialPlan,
            snapshot.ProjectionAnchorDate,
            1);
        var projection = projectionResult.Periods.Single();
        var planId = Guid.NewGuid();
        var lines = projection.MandatoryItems
            .Concat(projectionResult.FundingPlan.PreFirstSalaryObligations)
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

        foreach (var expense in projection.LargeExpenseItems)
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
        foreach (var card in projection.CardPaymentStatuses)
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

        return new PeriodPlanSnapshot
        {
            Id = planId,
            FinancialSnapshotId = snapshot.Id,
            PeriodStart = projection.PeriodStart,
            PeriodEnd = projection.PeriodEnd,
            ReviewAvailableFrom = projection.PeriodEnd,
            CreatedAtUtc = createdAtUtc,
            StrategyUsed = projection.PaymentAssignmentMode,
            PaymentWindowStart = projection.PaymentWindowStart,
            PaymentWindowEnd = projection.PaymentWindowEnd,
            OpeningSavings = projection.OpeningProjectedSavings,
            PlannedIncome = projection.TotalIncome,
            PlannedLoanPayments = projection.LoanPayments,
            PlannedCardPayments = projection.CreditCardPayments,
            PlannedTemporaryPayments = projection.TemporaryPayments,
            PlannedInstallmentPayments = projection.InstallmentPayments,
            PlannedOtherScheduledPayments = projection.OtherScheduledPayments,
            PlannedMandatoryPayments = projection.MandatoryOutflow,
            PlannedLivingBudget = projection.LivingBudget,
            PlannedLargeExpenses = projection.PlannedLargeCashExpenses,
            PlannedCardInterest = projection.CardInterestGenerated,
            PlannedDeficitInterest = projection.DeficitFinancingInterest,
            PlannedEndingSavings = projection.EndingProjectedSavings,
            PaymentLines = lines
                .OrderBy(x => x.PlannedDate)
                .ThenBy(x => x.Name)
                .ToArray()
        };
    }

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
