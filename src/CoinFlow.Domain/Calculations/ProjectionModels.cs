using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record CreditCardPaymentProjectionStatus(
    Guid CardId,
    string CardName,
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? Payment,
    CreditCardPaymentResolution Resolution,
    CreditCardPaymentType? PaymentType,
    DateOnly AssignedSalaryDate = default,
    bool PaymentBeforeSalary = false);

public sealed record SalaryPeriodProjection(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal SalaryIncome,
    decimal OtherIncome,
    decimal TotalIncome,
    decimal LoanPayments,
    decimal CreditCardPayments,
    decimal TemporaryPayments,
    decimal InstallmentPayments,
    decimal OtherScheduledPayments,
    decimal MandatoryOutflow,
    decimal AvailableAfterMandatory,
    decimal LivingBudget,
    decimal EstimatedSavingsCapacity,
    decimal PlannedLargeCashExpenses,
    decimal OpeningProjectedSavings,
    decimal EndingProjectedSavings,
    bool IsEstimatedCardPayment,
    bool HasUndeterminedCardPayment,
    bool HasDeficit,
    IReadOnlyList<IncomeProjectionItem> IncomeItems,
    IReadOnlyList<ObligationItem> MandatoryItems,
    IReadOnlyList<PlannedLargeExpense> LargeExpenseItems,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses,
    PaymentAssignmentMode PaymentAssignmentMode =
        PaymentAssignmentMode.UpcomingPeriod,
    DateOnly PaymentWindowStart = default,
    DateOnly PaymentWindowEnd = default)
{
    public SalaryPeriod Period => new(PeriodStart, PeriodEnd);
}
