using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record PeriodReviewAvailability(
    bool HasCurrentSnapshot,
    bool IsDue,
    FinancialSnapshot? CurrentSnapshot,
    PeriodPlanSnapshot? PendingPlan,
    DateOnly? LastUpdatedDate,
    string Message);

public sealed record ActualPaymentDraft(
    Guid PeriodPlanPaymentLineId,
    ActualPaymentStatus Status,
    decimal ActualAmount,
    DateOnly? ActualPaymentDate,
    string Note = "");

public sealed record ActualFlowDraft(
    ActualFlowType Type,
    string Name,
    string Category,
    DateOnly Date,
    decimal Amount);

public sealed record LivingBreakdownDraft(string Category, decimal Amount);

public sealed record PeriodReviewDraft(
    Guid PeriodPlanSnapshotId,
    decimal? RevisedLivingBudget,
    IReadOnlyList<ActualPaymentDraft> Payments,
    decimal ActualLivingSpend,
    decimal ActualInterest,
    IReadOnlyList<ActualFlowDraft> Flows,
    IReadOnlyList<LivingBreakdownDraft> LivingBreakdown,
    decimal? ConfirmedStartingSavings,
    string RevisionNote = "",
    string ActualNote = "");

public sealed record PlanActualComparisonLine(
    string Category,
    decimal Planned,
    decimal Actual,
    decimal Difference);

public sealed record PlanActualComparison(
    decimal PlannedEndingSavings,
    decimal ActualEndingSavings,
    decimal Difference,
    string Summary,
    IReadOnlyList<PlanActualComparisonLine> Lines);

public sealed record PeriodReviewContext(
    FinancialSnapshot Snapshot,
    PeriodPlanSnapshot OriginalPlan,
    PeriodPlanRevision? Revision,
    PeriodActual? Actual,
    decimal SuggestedStartingSavings,
    PlanActualComparison? Comparison);

public sealed record PeriodReviewPreview(
    decimal SuggestedStartingSavings,
    decimal ConfirmedStartingSavings,
    decimal ReconciliationAdjustment,
    PlanActualComparison Comparison);

public sealed record FinancialReviewResult(
    FinancialSnapshot NewSnapshot,
    PeriodActual Actual,
    PlanActualComparison Comparison,
    PeriodPlanSnapshot NewPlan);

public sealed record HistoryPeriod(
    PeriodPlanSnapshot OriginalPlan,
    PeriodPlanRevision? Revision,
    PeriodActual Actual,
    FinancialSnapshot ResultSnapshot,
    PlanActualComparison Comparison);

public sealed record HistorySummary(
    decimal Planned,
    decimal Actual,
    decimal Difference,
    int PeriodCount);
