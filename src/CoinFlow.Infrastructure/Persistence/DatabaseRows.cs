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
    public int PaymentAssignmentMode { get; set; }
    public int SchemaVersion { get; set; }
    public int DevelopmentSeedVersion { get; set; }

    // Legacy columns remain mapped so upgrades can write existing NOT NULL tables.
    [Column("GamificationEnabled")]
    public bool LegacyRemovedFeatureFlag { get; set; }
    public bool DevelopmentSeedEnabled { get; set; }
    public string? TrackingStartedDate { get; set; }
}
