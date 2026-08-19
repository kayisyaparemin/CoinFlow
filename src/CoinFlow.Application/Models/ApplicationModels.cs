using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record DashboardSnapshot(
    SalaryPeriodSummary SalaryPeriod,
    DailyCoinSnapshot DailyCoin,
    FutureMonthProjection NextSalaryPeriod,
    EmergencyFund EmergencyFund,
    bool GamificationEnabled,
    string Encouragement,
    CalculationDetails Details,
    UpcomingCardPayment? UpcomingCardPayment);

public sealed record UpcomingCardPayment(
    Guid CardId,
    string CardName,
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? PlannedPayment,
    CreditCardPaymentResolution Resolution,
    CreditCardPaymentType? PaymentType);

public sealed record CalculationDetails(
    SalaryPeriod CurrentPeriod,
    SpendableBalanceSource BalanceSource,
    DateOnly? SnapshotDate,
    decimal SnapshotOrStartAmount,
    decimal EligibleSpending,
    decimal CurrentAvailable,
    int RemainingDays,
    decimal SustainableDaily,
    DateOnly? NextCardStatementClose,
    DateOnly? NextCardPaymentDue,
    decimal? NextCardStatementBalance,
    decimal? NextCardMinimumPayment,
    decimal? NextCardPayment,
    CreditCardPaymentResolution? NextCardPaymentResolution);

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
    IReadOnlyList<SpendableBalanceSnapshot> SpendableBalanceSnapshots,
    EmergencyFund EmergencyFund,
    IReadOnlyList<EmergencyFundTransfer> EmergencyFundTransfers);
