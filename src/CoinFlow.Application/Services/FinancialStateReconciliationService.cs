using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class FinancialStateReconciliationService
{
    public decimal CalculateSuggestedSavings(
        FinancialSnapshot sourceSnapshot,
        decimal plannedIncome,
        IEnumerable<ActualPayment> payments,
        decimal actualLivingSpend,
        decimal actualInterest,
        IEnumerable<ActualFlow> flows)
    {
        if (actualLivingSpend < 0m || actualInterest < 0m)
        {
            throw new InvalidOperationException(
                "Gerçekleşen tutarlar negatif olamaz.");
        }

        var rows = flows.ToArray();
        var unplannedIncome = rows
            .Where(x => x.Type == ActualFlowType.UnplannedIncome)
            .Sum(x => x.Amount);
        var unplannedPayments = rows
            .Where(x => x.Type == ActualFlowType.UnplannedPayment)
            .Sum(x => x.Amount);

        return sourceSnapshot.ProjectionStartingSavings
               + plannedIncome
               + unplannedIncome
               - payments.Sum(x => x.ActualAmount)
               - actualLivingSpend
               - actualInterest
               - unplannedPayments;
    }
}
