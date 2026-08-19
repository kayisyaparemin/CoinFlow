using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record DashboardSnapshot(
    SalaryPeriodSummary SalaryPeriod,
    DailyCoinSnapshot DailyCoin,
    EmergencyFund EmergencyFund,
    bool GamificationEnabled,
    string Encouragement);

public sealed record ExpenseDraft(
    decimal Amount,
    DateOnly Date,
    ExpenseCategory Category,
    ExpensePaymentType PaymentType,
    string Note,
    Guid? CreditCardId = null,
    int? InstallmentCount = null,
    DateOnly? FirstInstallmentDate = null);

public sealed record FinanceData(
    UserSettings Settings,
    IReadOnlyList<SalaryScheduleEntry> Salaries,
    IReadOnlyList<Loan> Loans,
    IReadOnlyList<TemporaryPaymentPlan> PaymentPlans,
    IReadOnlyList<CreditCard> CreditCards,
    IReadOnlyList<Expense> Expenses,
    EmergencyFund EmergencyFund);
