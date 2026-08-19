using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record EmergencyContributionPlan(
    DateOnly SalaryPeriodStart,
    decimal ReservedAmount,
    decimal FulfilledAmount,
    decimal RemainingReservedAmount);

public sealed record EmergencyTransferAllocation(
    decimal CoveredPlannedAmount,
    decimal ExtraSpendableAmount);

public sealed class EmergencyFundCalculator
{
    public EmergencyContributionPlan CalculateCurrentPeriod(
        EmergencyFund fund,
        DateOnly periodStart,
        IEnumerable<EmergencyFundTransfer> transfers)
    {
        var fulfilled = transfers
            .Where(x => x.SalaryPeriodStart == periodStart)
            .Sum(x => x.CoveredPlannedAmount);
        var remainingPlan = Math.Max(0m, fund.PlannedPeriodContribution - fulfilled);
        var remainingTarget = Math.Max(0m, fund.TargetAmount - fund.CurrentAmount);
        var remainingReserved = Math.Min(remainingPlan, remainingTarget);
        return new EmergencyContributionPlan(
            periodStart,
            fulfilled + remainingReserved,
            fulfilled,
            remainingReserved);
    }

    public IReadOnlyList<decimal> ProjectContributions(
        EmergencyFund fund,
        IReadOnlyList<SalaryPeriod> periods,
        IEnumerable<EmergencyFundTransfer> transfers)
    {
        if (periods.Count == 0)
        {
            return [];
        }

        var result = new decimal[periods.Count];
        var current = CalculateCurrentPeriod(fund, periods[0].Start, transfers);
        result[0] = current.ReservedAmount;
        var projectedFund = fund.CurrentAmount + current.RemainingReservedAmount;

        for (var index = 1; index < periods.Count; index++)
        {
            var remainingTarget = Math.Max(0m, fund.TargetAmount - projectedFund);
            var contribution = Math.Min(fund.PlannedPeriodContribution, remainingTarget);
            result[index] = contribution;
            projectedFund += contribution;
        }

        return result;
    }

    public EmergencyTransferAllocation AllocateTransfer(
        EmergencyFund fund,
        DateOnly periodStart,
        decimal amount,
        IEnumerable<EmergencyFundTransfer> existingTransfers)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Aktarım tutarı sıfırdan büyük olmalıdır.");
        }

        var fulfilled = existingTransfers
            .Where(x => x.SalaryPeriodStart == periodStart)
            .Sum(x => x.CoveredPlannedAmount);
        var plannedCapacity = Math.Max(0m, fund.PlannedPeriodContribution - fulfilled);
        var targetCapacity = Math.Max(0m, fund.TargetAmount - fund.CurrentAmount);
        var covered = Math.Min(amount, Math.Min(plannedCapacity, targetCapacity));
        return new EmergencyTransferAllocation(covered, amount - covered);
    }
}
