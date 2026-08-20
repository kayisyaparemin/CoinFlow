using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum ObligationType
{
    Loan,
    CreditCard,
    TemporaryPayment,
    InstallmentPayment,
    OtherScheduledPayment
}

public sealed record ObligationItem(
    string Name,
    ObligationType Type,
    DateOnly DueDate,
    decimal Amount,
    bool IsFinalPayment = false,
    bool IsEstimate = false,
    string Detail = "",
    DateOnly AssignedSalaryDate = default,
    bool PaymentBeforeSalary = false);

public sealed record MandatoryPaymentSummary(
    IReadOnlyList<ObligationItem> Items,
    decimal LoanPayments,
    decimal CreditCardPayments,
    decimal TemporaryPayments,
    decimal InstallmentPayments,
    decimal OtherScheduledPayments,
    decimal Total);

public sealed class MandatoryPaymentCalculator(
    LoanScheduleCalculator loanScheduleCalculator,
    ScheduledPaymentCalculator scheduledPaymentCalculator,
    PaymentAssignmentResolver assignmentResolver)
{
    public MandatoryPaymentSummary Calculate(
        SalaryPeriod period,
        IEnumerable<Loan> loans,
        IEnumerable<TemporaryPaymentPlan> plans,
        IEnumerable<ObligationItem> creditCardPayments,
        int salaryDay,
        PaymentAssignmentMode assignmentMode)
    {
        var items = new List<ObligationItem>();

        foreach (var loan in loans.Where(x => x.IsActive))
        {
            var dates = loanScheduleCalculator.GetPaymentDates(loan);
            var finalDate = dates.LastOrDefault();
            items.AddRange(dates
                .Select(date => new ObligationItem(
                    $"{loan.Bank} {loan.Name}".Trim(),
                    ObligationType.Loan,
                    date,
                    loan.MonthlyPayment,
                    date == finalDate)));
        }

        items.AddRange(scheduledPaymentCalculator.GetItems(plans));
        items.AddRange(creditCardPayments);

        var ordered = items
            .Select(item => Assign(item, salaryDay, assignmentMode))
            .Where(item => item.AssignedSalaryDate == period.Start)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Name)
            .ToArray();
        decimal Sum(ObligationType type) =>
            ordered.Where(x => x.Type == type).Sum(x => x.Amount);

        var loanPayments = Sum(ObligationType.Loan);
        var cardPayments = Sum(ObligationType.CreditCard);
        var temporaryPayments = Sum(ObligationType.TemporaryPayment);
        var installmentPayments = Sum(ObligationType.InstallmentPayment);
        var otherPayments = Sum(ObligationType.OtherScheduledPayment);
        return new MandatoryPaymentSummary(
            ordered,
            loanPayments,
            cardPayments,
            temporaryPayments,
            installmentPayments,
            otherPayments,
            loanPayments + cardPayments + temporaryPayments +
            installmentPayments + otherPayments);
    }

    private ObligationItem Assign(
        ObligationItem item,
        int salaryDay,
        PaymentAssignmentMode assignmentMode)
    {
        var salaryDate = assignmentResolver.ResolveFundingSalaryDate(
            item.DueDate,
            salaryDay,
            assignmentMode);
        return item with
        {
            AssignedSalaryDate = salaryDate,
            PaymentBeforeSalary = item.DueDate < salaryDate
        };
    }
}
