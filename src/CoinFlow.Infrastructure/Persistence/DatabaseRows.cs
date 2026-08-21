using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

[Table("salary_schedule")]
internal sealed class SalaryRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    [Indexed] public string EffectiveFrom { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("other_incomes")]
internal sealed class OtherIncomeRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    [Indexed] public string ExactDate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

[Table("loans")]
internal sealed class LoanRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Bank { get; set; } = string.Empty;
    public decimal MonthlyInstallment { get; set; }
    public int PaymentDay { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string? EndDate { get; set; }
    public int? InstallmentCount { get; set; }
    public decimal? RemainingDebt { get; set; }
    public decimal? EarlyClosureAmount { get; set; }
    public bool IsActive { get; set; }
}

[Table("payment_plans")]
internal sealed class PaymentPlanRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Kind { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? TotalRepaymentAmount { get; set; }
}

[Table("payment_installments")]
internal sealed class PaymentInstallmentRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PlanId { get; set; } = string.Empty;
    [Indexed] public string DueDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
}

[Table("credit_cards")]
internal sealed class CreditCardRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Bank { get; set; } = string.Empty;
    public decimal Limit { get; set; }
    public decimal CurrentTotalDebt { get; set; }
    public decimal LastStatementDebt { get; set; }
    public decimal LastStatementRemaining { get; set; }
    public decimal CurrentCycleSpending { get; set; }
    public int StatementClosingDay { get; set; }
    public int PaymentDueDay { get; set; }
    public decimal MinimumPaymentRate { get; set; }
    public int PaymentMode { get; set; }
    public decimal? ManualPaymentAmount { get; set; }
    public decimal CarriedBalance { get; set; }
    public decimal UnbilledSpending { get; set; }
    public string BalanceAsOfDate { get; set; } = string.Empty;
    public int StatementModelVersion { get; set; }
    public int PaymentStrategy { get; set; }
    public decimal? FixedPaymentAmount { get; set; }
    public int ProjectionFallbackStrategy { get; set; }
    public decimal? ProjectionFallbackFixedAmount { get; set; }
}

[Table("card_installments")]
internal sealed class CardInstallmentRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string CreditCardId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [Indexed] public string DueDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[Table("credit_card_payment_plans")]
internal sealed class CreditCardPaymentPlanRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string CreditCardId { get; set; } = string.Empty;
    [Indexed] public string DueDate { get; set; } = string.Empty;
    public decimal PlannedPaymentAmount { get; set; }
    public int PaymentType { get; set; }
    public decimal? Amount { get; set; }
}

[Table("planned_large_expenses")]
internal sealed class PlannedLargeExpenseRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    [Indexed] public string ExactDate { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Status { get; set; }
}

[Table("settings")]
internal sealed class SettingsRow
{
    [PrimaryKey] public int Id { get; set; } = 1;
    public int SalaryDay { get; set; }
    public decimal MonthlyLivingBudget { get; set; }
    public decimal ProjectionStartingSavings { get; set; }
    public string? ProjectionAnchorDate { get; set; }
    public decimal CreditCardCarryInterestRate { get; set; }
    public decimal DeficitFinancingInterestRate { get; set; }
    // Legacy v5 source used only to bootstrap strategy history once.
    public int PaymentAssignmentMode { get; set; }
    public int SchemaVersion { get; set; }
    public int DevelopmentSeedVersion { get; set; }

    // Legacy columns remain mapped so upgrades can write existing NOT NULL tables.
    [Column("GamificationEnabled")]
    public bool LegacyRemovedFeatureFlag { get; set; }
    public bool DevelopmentSeedEnabled { get; set; }
    public string? TrackingStartedDate { get; set; }
}

[Table("payment_assignment_strategies")]
internal sealed class PaymentAssignmentStrategyRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    public int Mode { get; set; }
    [Indexed(Unique = true)]
    public string EffectiveFromSalaryDate { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("financial_snapshots")]
internal sealed class FinancialSnapshotRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string SnapshotDate { get; set; } = string.Empty;
    public string ProjectionAnchorDate { get; set; } = string.Empty;
    [Indexed] public string NextReviewDate { get; set; } = string.Empty;
    public decimal ProjectionStartingSavings { get; set; }
    public int SalaryDay { get; set; }
    public string? PreviousSnapshotId { get; set; }
    public int Source { get; set; }
    [Indexed] public bool IsCurrent { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("period_plan_snapshots")]
internal sealed class PeriodPlanSnapshotRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string FinancialSnapshotId { get; set; } = string.Empty;
    [Indexed] public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    [Indexed] public string ReviewAvailableFrom { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public int StrategyUsed { get; set; }
    public string PaymentWindowStart { get; set; } = string.Empty;
    public string PaymentWindowEnd { get; set; } = string.Empty;
    public decimal OpeningSavings { get; set; }
    public decimal PlannedIncome { get; set; }
    public decimal PlannedLoanPayments { get; set; }
    public decimal PlannedCardPayments { get; set; }
    public decimal PlannedTemporaryPayments { get; set; }
    public decimal PlannedInstallmentPayments { get; set; }
    public decimal PlannedOtherScheduledPayments { get; set; }
    public decimal PlannedMandatoryPayments { get; set; }
    public decimal PlannedLivingBudget { get; set; }
    public decimal PlannedLargeExpenses { get; set; }
    public decimal PlannedCardInterest { get; set; }
    public decimal PlannedDeficitInterest { get; set; }
    public decimal PlannedEndingSavings { get; set; }
}

[Table("period_plan_payment_lines")]
internal sealed class PeriodPlanPaymentLineRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public int SourceType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlannedDate { get; set; } = string.Empty;
    public decimal? PlannedAmount { get; set; }
    public bool IsEstimate { get; set; }
    public string Detail { get; set; } = string.Empty;
}

[Table("period_plan_revisions")]
internal sealed class PeriodPlanRevisionRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public decimal PlannedIncome { get; set; }
    public decimal PlannedMandatoryPayments { get; set; }
    public decimal PlannedLivingBudget { get; set; }
    public decimal PlannedLargeExpenses { get; set; }
    public decimal PlannedInterest { get; set; }
    public decimal PlannedEndingSavings { get; set; }
    public string Note { get; set; } = string.Empty;
}

[Table("period_actuals")]
internal sealed class PeriodActualRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed(Unique = true)] public string PeriodPlanSnapshotId { get; set; } = string.Empty;
    public string SourceFinancialSnapshotId { get; set; } = string.Empty;
    public string ResultFinancialSnapshotId { get; set; } = string.Empty;
    [Indexed] public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public string FinalizedAtUtc { get; set; } = string.Empty;
    public decimal ActualIncome { get; set; }
    public decimal ActualLoanPayments { get; set; }
    public decimal ActualCardPayments { get; set; }
    public decimal ActualTemporaryPayments { get; set; }
    public decimal ActualInstallmentPayments { get; set; }
    public decimal ActualOtherScheduledPayments { get; set; }
    public decimal ActualLargeExpenses { get; set; }
    public decimal ActualMandatoryPayments { get; set; }
    public decimal ActualLivingSpend { get; set; }
    public decimal ActualInterest { get; set; }
    public decimal UnplannedIncome { get; set; }
    public decimal UnplannedPayments { get; set; }
    public decimal DerivedEndingSavings { get; set; }
    public decimal ConfirmedEndingSavings { get; set; }
    public decimal ReconciliationAdjustment { get; set; }
    public string ComparisonSummary { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

[Table("actual_payments")]
internal sealed class ActualPaymentRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public string PeriodPlanPaymentLineId { get; set; } = string.Empty;
    public string SourceEntityId { get; set; } = string.Empty;
    public int SourceType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlannedDate { get; set; } = string.Empty;
    public decimal? PlannedAmount { get; set; }
    public string? ActualPaymentDate { get; set; }
    public decimal ActualAmount { get; set; }
    public int Status { get; set; }
    public string Note { get; set; } = string.Empty;
}

[Table("actual_flows")]
internal sealed class ActualFlowRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[Table("actual_living_breakdowns")]
internal sealed class ActualLivingBreakdownRow
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public string PeriodActualId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
