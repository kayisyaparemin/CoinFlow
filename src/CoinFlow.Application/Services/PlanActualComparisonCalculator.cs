using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class PlanActualComparisonCalculator
{
    public PlanActualComparison Calculate(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision? revision,
        PeriodActual actual)
    {
        var plannedIncome = revision?.PlannedIncome ?? plan.PlannedIncome;
        var plannedMandatory = revision?.PlannedMandatoryPayments ??
                               plan.PlannedMandatoryPayments;
        var plannedLiving = revision?.PlannedLivingBudget ??
                            plan.PlannedLivingBudget;
        var plannedLarge = revision?.PlannedLargeExpenses ??
                           plan.PlannedLargeExpenses;
        var plannedInterest = revision?.PlannedInterest ??
                              plan.PlannedCardInterest +
                              plan.PlannedDeficitInterest;
        var plannedEnding = revision?.PlannedEndingSavings ??
                            plan.PlannedEndingSavings;
        var lines = new[]
        {
            Line("Gelir", plannedIncome, actual.ActualIncome),
            Line("Krediler", plan.PlannedLoanPayments,
                actual.ActualLoanPayments),
            Line("Kredi kartları", plan.PlannedCardPayments,
                actual.ActualCardPayments),
            Line("Geçici ödemeler", plan.PlannedTemporaryPayments,
                actual.ActualTemporaryPayments),
            Line("Taksitli ödemeler", plan.PlannedInstallmentPayments,
                actual.ActualInstallmentPayments),
            Line("Diğer planlı ödemeler",
                plan.PlannedOtherScheduledPayments,
                actual.ActualOtherScheduledPayments),
            Line("Zorunlu ödemeler", plannedMandatory,
                actual.ActualMandatoryPayments),
            Line("Büyük ödemeler", plannedLarge,
                actual.ActualLargeExpenses),
            Line("Yaşam giderleri", plannedLiving,
                actual.ActualLivingSpend),
            Line("Faiz", plannedInterest, actual.ActualInterest),
            Line("Plan dışı ödemeler", 0m,
                actual.UnplannedPayments),
            Line("Dönem düzeltmesi", 0m,
                actual.ReconciliationAdjustment)
        };
        var difference = actual.ConfirmedEndingSavings - plannedEnding;
        return new PlanActualComparison(
            plannedEnding,
            actual.ConfirmedEndingSavings,
            difference,
            BuildSummary(difference, lines),
            lines);
    }

    private static PlanActualComparisonLine Line(
        string category,
        decimal planned,
        decimal actual) =>
        new(category, planned, actual, actual - planned);

    private static string BuildSummary(
        decimal endingDifference,
        IReadOnlyList<PlanActualComparisonLine> lines)
    {
        if (endingDifference == 0m)
        {
            return "Bu dönem planlanan dönem sonu birikimiyle aynı seviyede tamamlandı.";
        }

        var direction = endingDifference > 0m ? "üzerinde" : "altında";
        var lead =
            $"Dönem sonu birikimi planın {Math.Abs(endingDifference):N2} TL {direction} gerçekleşti.";
        var cause = lines
            .Where(x =>
                x.Category is not "Dönem düzeltmesi" and
                not "Zorunlu ödemeler" &&
                x.Difference != 0m)
            .OrderByDescending(x => Math.Abs(x.Difference))
            .FirstOrDefault();
        return cause is null
            ? lead
            : $"{lead} En belirgin fark {cause.Category} kaleminde {Math.Abs(cause.Difference):N2} TL oldu.";
    }
}
