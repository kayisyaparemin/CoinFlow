namespace CoinFlow.Domain.Models;

public enum CreditCardPaymentMode
{
    Minimum = 0,
    Manual = 1
}

public enum ExpensePaymentType
{
    Cash = 0,
    CreditCard = 1,
    NewInstallment = 2,
    Other = 3
}

public enum ExpenseCategory
{
    Food = 0,
    Fuel = 1,
    Grocery = 2,
    Car = 3,
    Entertainment = 4,
    Home = 5,
    Gift = 6,
    Bill = 7,
    Other = 8
}

public enum PaymentPlanKind
{
    Temporary = 0,
    PlannedInstallment = 1
}

public sealed record SalaryScheduleEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal NetAmount { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed record Loan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Bank { get; init; } = string.Empty;
    public decimal MonthlyInstallment { get; init; }
    public int PaymentDay { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int? InstallmentCount { get; init; }
    public decimal? RemainingDebt { get; init; }
    public decimal? EarlyClosureAmount { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record TemporaryPaymentPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public PaymentPlanKind Kind { get; init; }
    public IReadOnlyList<TemporaryPaymentInstallment> Installments { get; init; } = [];
}

public sealed record TemporaryPaymentInstallment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlanId { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal Amount { get; init; }
    public bool IsPaid { get; init; }
}

public sealed record CreditCard
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Bank { get; init; } = string.Empty;
    public decimal Limit { get; init; }
    public decimal CurrentTotalDebt { get; init; }
    public decimal CarriedBalance { get; init; }
    public decimal UnbilledSpending { get; init; }
    public DateOnly BalanceAsOfDate { get; init; }
    public int StatementClosingDay { get; init; }
    public int PaymentDueDay { get; init; }
    public decimal MinimumPaymentRate { get; init; }
    public IReadOnlyList<CardCharge> Charges { get; init; } = [];
    public IReadOnlyList<CreditCardPaymentPlan> PaymentPlans { get; init; } = [];
}

public sealed record CardCharge
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CreditCardId { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateOnly PostingDate { get; init; }
    public decimal Amount { get; init; }
}

public sealed record CreditCardPaymentPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CreditCardId { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal PlannedPaymentAmount { get; init; }
}

public sealed record Expense
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal Amount { get; init; }
    public DateOnly Date { get; init; }
    public ExpenseCategory Category { get; init; }
    public ExpensePaymentType PaymentType { get; init; }
    public string Note { get; init; } = string.Empty;
    public Guid? CreditCardId { get; init; }
    public int? InstallmentCount { get; init; }
    public DateOnly? FirstInstallmentDate { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record SpendableBalanceSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal Amount { get; init; }
    public DateOnly SnapshotDate { get; init; }
    public DateOnly SalaryPeriodStart { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed record EmergencyFund
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal TargetAmount { get; init; }
    public decimal CurrentAmount { get; init; }
    public decimal PlannedPeriodContribution { get; init; }
}

public sealed record EmergencyFundTransfer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly TransferDate { get; init; }
    public DateOnly SalaryPeriodStart { get; init; }
    public decimal Amount { get; init; }
    public decimal CoveredPlannedAmount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record UserSettings
{
    public int SalaryDay { get; init; } = 10;
    public bool GamificationEnabled { get; init; } = true;
    public bool DevelopmentSeedEnabled { get; init; } = true;
    public DateOnly? TrackingStartedDate { get; init; }
}
