using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ICoinFlowStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ClearAllFinancialDataAsync(
        CancellationToken cancellationToken = default);
    Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default);

    Task<UserSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentAssignmentStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default);
    Task UpsertPaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        CancellationToken cancellationToken = default);
    Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(
        CancellationToken cancellationToken = default);
    Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default);
    Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OneTimeIncome>> GetOtherIncomesAsync(
        CancellationToken cancellationToken = default);
    Task UpsertOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default);
    Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Loan>> GetLoansAsync(
        CancellationToken cancellationToken = default);
    Task UpsertLoanAsync(Loan loan, CancellationToken cancellationToken = default);
    Task DeleteLoanAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemporaryPaymentPlan>> GetPaymentPlansAsync(
        CancellationToken cancellationToken = default);
    Task UpsertPaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default);
    Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(
        CancellationToken cancellationToken = default);
    Task UpsertCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default);
    Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlannedLargeExpense>> GetPlannedLargeExpensesAsync(
        CancellationToken cancellationToken = default);
    Task UpsertPlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default);
    Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
