using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record DashboardSnapshot(
    SalaryPeriodProjection CurrentPeriod,
    IReadOnlyList<ObligationItem> PreFirstSalaryObligations,
    IReadOnlyList<ObligationItem> UpcomingPayments,
    decimal TwelvePeriodEndingProjectedSavings,
    SalaryPeriodProjection TightestPeriod,
    bool HasUndeterminedCardPayments,
    PaymentAssignmentStrategy CurrentStrategy,
    PaymentAssignmentStrategy? PendingStrategy,
    DateOnly ProjectionAnchorDate);

public sealed record PaymentAssignmentStrategyOverview(
    PaymentAssignmentStrategy Current,
    PaymentAssignmentStrategy? Pending,
    IReadOnlyList<PaymentAssignmentStrategy> History,
    IReadOnlyList<DateOnly> AvailableEffectiveSalaryDates);

public sealed record PaymentStrategyChangePreview(
    DateOnly EffectiveSalaryDate,
    PaymentAssignmentMode CurrentMode,
    PaymentAssignmentMode NewMode,
    SalaryPeriodProjection Baseline,
    SalaryPeriodProjection Scenario)
{
    public decimal TotalTransitionBurden => Scenario.MandatoryOutflow;
    public decimal FinancingGap => Math.Min(
        0m,
        Scenario.EstimatedSavingsCapacity);
}
