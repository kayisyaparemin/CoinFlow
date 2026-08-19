using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Abstractions;

public interface ICoinFlowStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ResetAllDataAsync(CancellationToken cancellationToken = default);

    Task<UserSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(CancellationToken cancellationToken = default);
    Task UpsertSalaryAsync(SalaryScheduleEntry entry, CancellationToken cancellationToken = default);
    Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Loan>> GetLoansAsync(CancellationToken cancellationToken = default);
    Task UpsertLoanAsync(Loan loan, CancellationToken cancellationToken = default);
    Task DeleteLoanAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemporaryPaymentPlan>> GetPaymentPlansAsync(CancellationToken cancellationToken = default);
    Task UpsertPaymentPlanAsync(TemporaryPaymentPlan plan, CancellationToken cancellationToken = default);
    Task DeletePaymentPlanAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(CancellationToken cancellationToken = default);
    Task UpsertCreditCardAsync(CreditCard card, CancellationToken cancellationToken = default);
    Task DeleteCreditCardAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> GetExpensesAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);
    Task UpsertExpenseAsync(Expense expense, CancellationToken cancellationToken = default);
    Task DeleteExpenseAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpendableBalanceSnapshot>> GetSpendableBalanceSnapshotsAsync(CancellationToken cancellationToken = default);
    Task UpsertSpendableBalanceSnapshotAsync(SpendableBalanceSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<EmergencyFund> GetEmergencyFundAsync(CancellationToken cancellationToken = default);
    Task SaveEmergencyFundAsync(EmergencyFund emergencyFund, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmergencyFundTransfer>> GetEmergencyFundTransfersAsync(CancellationToken cancellationToken = default);
    Task UpsertEmergencyFundTransferAsync(EmergencyFundTransfer transfer, CancellationToken cancellationToken = default);
}
