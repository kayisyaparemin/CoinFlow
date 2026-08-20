namespace CoinFlow.Domain.Models;

public enum CreditCardPaymentStrategy
{
    AskEachStatement = 0,
    Minimum = 1,
    FullStatement = 2,
    FixedAmount = 3
}

public enum ProjectionFallbackStrategy
{
    None = 0,
    Minimum = 1,
    FullStatement = 2,
    FixedAmount = 3
}

public enum CreditCardPaymentType
{
    FixedAmount = 0,
    Minimum = 1,
    FullStatement = 2
}

public enum PaymentPlanKind
{
    Temporary = 0,
    Installment = 1,
    Recurring = 2,
    OtherScheduled = 3
}

public enum PlannedExpenseStatus
{
    Planned = 0,
    Completed = 1,
    Cancelled = 2
}

public sealed record SalaryScheduleEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal Amount { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed record OneTimeIncome
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal Amount { get; init; }
    public DateOnly ExactDate { get; init; }
    public string Description { get; init; } = string.Empty;
}

public sealed record Loan
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Bank { get; init; } = string.Empty;
    public decimal MonthlyPayment { get; init; }
    public int PaymentDay { get; init; }
    public DateOnly NextPaymentDate { get; init; }
    public int RemainingInstallmentCount { get; init; }
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
    public decimal CarriedBalance { get; init; }
    public decimal UnbilledSpending { get; init; }
    public DateOnly BalanceAsOfDate { get; init; }
    public int StatementClosingDay { get; init; }
    public int PaymentDueDay { get; init; }
    public decimal MinimumPaymentRate { get; init; }
    public CreditCardPaymentStrategy PaymentStrategy { get; init; } = CreditCardPaymentStrategy.AskEachStatement;
    public decimal? FixedPaymentAmount { get; init; }
    public ProjectionFallbackStrategy ProjectionFallbackStrategy { get; init; } = ProjectionFallbackStrategy.None;
    public decimal? ProjectionFallbackFixedAmount { get; init; }
    public IReadOnlyList<CardCharge> Charges { get; init; } = [];
    public IReadOnlyList<CreditCardPaymentPlan> PaymentPlans { get; init; } = [];

    public decimal KnownTotalDebt => CarriedBalance + UnbilledSpending + Charges.Sum(x => x.Amount);
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
    public CreditCardPaymentType PaymentType { get; init; }
    public decimal? Amount { get; init; }
}

public sealed record PlannedLargeExpense
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateOnly ExactDate { get; init; }
    public string Note { get; init; } = string.Empty;
    public PlannedExpenseStatus Status { get; init; } = PlannedExpenseStatus.Planned;
}

public sealed record UserSettings
{
    public int SalaryDay { get; init; } = 10;
    public decimal MonthlyLivingBudget { get; init; }
    public decimal ProjectionStartingSavings { get; init; }
}

public sealed record FinancialPlan
{
    public UserSettings Settings { get; init; } = new();
    public IReadOnlyList<SalaryScheduleEntry> Salaries { get; init; } = [];
    public IReadOnlyList<OneTimeIncome> OtherIncomes { get; init; } = [];
    public IReadOnlyList<Loan> Loans { get; init; } = [];
    public IReadOnlyList<TemporaryPaymentPlan> PaymentPlans { get; init; } = [];
    public IReadOnlyList<CreditCard> CreditCards { get; init; } = [];
    public IReadOnlyList<PlannedLargeExpense> PlannedLargeExpenses { get; init; } = [];
}
