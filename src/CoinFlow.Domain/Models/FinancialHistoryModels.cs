namespace CoinFlow.Domain.Models;

public enum FinancialSnapshotSource
{
    Initial = 0,
    MonthlyUpdate = 1,
    Recovery = 2
}

public enum PlanPaymentSourceType
{
    Loan = 0,
    CreditCard = 1,
    TemporaryPayment = 2,
    InstallmentPayment = 3,
    OtherScheduledPayment = 4,
    PlannedLargeExpense = 5
}

public enum ActualPaymentStatus
{
    Paid = 0,
    DifferentAmount = 1,
    Unpaid = 2
}

public enum ActualFlowType
{
    UnplannedPayment = 0,
    UnplannedIncome = 1
}

public sealed record FinancialSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly SnapshotDate { get; init; }
    public DateOnly ProjectionAnchorDate { get; init; }
    public DateOnly NextReviewDate { get; init; }
    public decimal ProjectionStartingSavings { get; init; }
    public int SalaryDay { get; init; }
    public Guid? PreviousSnapshotId { get; init; }
    public FinancialSnapshotSource Source { get; init; }
    public bool IsCurrent { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed record PeriodPlanSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FinancialSnapshotId { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public DateOnly ReviewAvailableFrom { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public PaymentAssignmentMode StrategyUsed { get; init; }
    public DateOnly PaymentWindowStart { get; init; }
    public DateOnly PaymentWindowEnd { get; init; }
    public decimal OpeningSavings { get; init; }
    public decimal PlannedIncome { get; init; }
    public decimal PlannedLoanPayments { get; init; }
    public decimal PlannedCardPayments { get; init; }
    public decimal PlannedTemporaryPayments { get; init; }
    public decimal PlannedInstallmentPayments { get; init; }
    public decimal PlannedOtherScheduledPayments { get; init; }
    public decimal PlannedMandatoryPayments { get; init; }
    public decimal PlannedLivingBudget { get; init; }
    public decimal PlannedLargeExpenses { get; init; }
    public decimal PlannedCardInterest { get; init; }
    public decimal PlannedDeficitInterest { get; init; }
    public decimal PlannedEndingSavings { get; init; }
    public IReadOnlyList<PeriodPlanPaymentLine> PaymentLines { get; init; } = [];
}

public sealed record PeriodPlanPaymentLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodPlanSnapshotId { get; init; }
    public Guid SourceEntityId { get; init; }
    public PlanPaymentSourceType SourceType { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly PlannedDate { get; init; }
    public decimal? PlannedAmount { get; init; }
    public bool IsEstimate { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed record PeriodPlanRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodPlanSnapshotId { get; init; }
    public int RevisionNumber { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Trigger { get; init; } = string.Empty;
    public PaymentAssignmentMode StrategyUsed { get; init; }
    public decimal PlannedIncome { get; init; }
    public decimal PlannedLoanPayments { get; init; }
    public decimal PlannedCardPayments { get; init; }
    public decimal PlannedTemporaryPayments { get; init; }
    public decimal PlannedInstallmentPayments { get; init; }
    public decimal PlannedOtherScheduledPayments { get; init; }
    public decimal PlannedMandatoryPayments { get; init; }
    public decimal PlannedLivingBudget { get; init; }
    public decimal PlannedLargeExpenses { get; init; }
    public decimal PlannedCardInterest { get; init; }
    public decimal PlannedDeficitInterest { get; init; }
    public decimal PlannedInterest { get; init; }
    public decimal PlannedEndingSavings { get; init; }
    public string Note { get; init; } = string.Empty;
    public IReadOnlyList<PeriodPlanPaymentLine> PaymentLines { get; init; } = [];
}

public sealed record PeriodActual
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodPlanSnapshotId { get; init; }
    public Guid SourceFinancialSnapshotId { get; init; }
    public Guid ResultFinancialSnapshotId { get; init; }
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public DateTimeOffset FinalizedAtUtc { get; init; }
    public decimal ActualIncome { get; init; }
    public decimal ActualLoanPayments { get; init; }
    public decimal ActualCardPayments { get; init; }
    public decimal ActualTemporaryPayments { get; init; }
    public decimal ActualInstallmentPayments { get; init; }
    public decimal ActualOtherScheduledPayments { get; init; }
    public decimal ActualLargeExpenses { get; init; }
    public decimal ActualMandatoryPayments { get; init; }
    public decimal ActualLivingSpend { get; init; }
    public decimal ActualInterest { get; init; }
    public decimal UnplannedIncome { get; init; }
    public decimal UnplannedPayments { get; init; }
    public decimal DerivedEndingSavings { get; init; }
    public decimal ConfirmedEndingSavings { get; init; }
    public decimal ReconciliationAdjustment { get; init; }
    public string ComparisonSummary { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public IReadOnlyList<ActualPayment> Payments { get; init; } = [];
    public IReadOnlyList<ActualFlow> Flows { get; init; } = [];
    public IReadOnlyList<ActualLivingBreakdown> LivingBreakdown { get; init; } = [];
}

public sealed record ActualPayment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodActualId { get; init; }
    public Guid PeriodPlanPaymentLineId { get; init; }
    public Guid SourceEntityId { get; init; }
    public PlanPaymentSourceType SourceType { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly PlannedDate { get; init; }
    public decimal? PlannedAmount { get; init; }
    public DateOnly? ActualPaymentDate { get; init; }
    public decimal ActualAmount { get; init; }
    public ActualPaymentStatus Status { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed record ActualFlow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodActualId { get; init; }
    public ActualFlowType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public decimal Amount { get; init; }
}

public sealed record ActualLivingBreakdown
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PeriodActualId { get; init; }
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public sealed record FinancialHistoryData(
    IReadOnlyList<FinancialSnapshot> Snapshots,
    IReadOnlyList<PeriodPlanSnapshot> Plans,
    IReadOnlyList<PeriodPlanRevision> Revisions,
    IReadOnlyList<PeriodActual> Actuals);

public sealed record FinancialReviewCommit(
    PeriodPlanRevision? Revision,
    PeriodActual Actual,
    FinancialSnapshot NewSnapshot,
    PeriodPlanSnapshot NewPlan,
    UserSettings UpdatedSettings,
    IReadOnlyList<Loan> UpdatedLoans,
    IReadOnlyList<TemporaryPaymentPlan> UpdatedPaymentPlans,
    IReadOnlyList<CreditCard> UpdatedCreditCards,
    IReadOnlyList<PlannedLargeExpense> UpdatedLargeExpenses);
