using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record MandatoryPaymentSummary(
    IReadOnlyList<ObligationItem> Items,
    decimal LoanPayments,
    decimal CreditCardPayments,
    decimal TemporaryPayments,
    decimal PlannedInstallments,
    decimal EmergencyFundContribution,
    decimal Total,
    decimal ProjectedTotal);

public sealed class MandatoryPaymentCalculator(LoanScheduleCalculator loanScheduleCalculator)
{
    public MandatoryPaymentSummary Calculate(
        SalaryPeriod period,
        IEnumerable<Loan> loans,
        IEnumerable<TemporaryPaymentPlan> plans,
        IEnumerable<ObligationItem> creditCardPayments,
        decimal emergencyFundContribution)
    {
        var items = new List<ObligationItem>();

        foreach (var loan in loans.Where(x => x.IsActive))
        {
            var dates = loanScheduleCalculator.GetPaymentDates(loan);
            var finalDate = dates.LastOrDefault();
            items.AddRange(dates
                .Where(period.Contains)
                .Select(date => new ObligationItem(
                    $"{loan.Bank} {loan.Name}".Trim(),
                    ObligationType.Loan,
                    date,
                    loan.MonthlyInstallment,
                    date == finalDate)));
        }

        foreach (var plan in plans)
        {
            var unpaid = plan.Installments.Where(x => !x.IsPaid).OrderBy(x => x.DueDate).ToArray();
            var finalDate = unpaid.LastOrDefault()?.DueDate;
            var type = plan.Kind == PaymentPlanKind.Temporary
                ? ObligationType.TemporaryPayment
                : ObligationType.PlannedInstallment;
            items.AddRange(unpaid
                .Where(x => period.Contains(x.DueDate))
                .Select(x => new ObligationItem(plan.Name, type, x.DueDate, x.Amount, x.DueDate == finalDate)));
        }

        items.AddRange(creditCardPayments.Where(x => period.Contains(x.DueDate)));
        if (emergencyFundContribution > 0m)
        {
            items.Add(new ObligationItem(
                "Acil durum tamponu",
                ObligationType.EmergencyFund,
                period.Start,
                emergencyFundContribution));
        }

        var ordered = items.OrderBy(x => x.DueDate).ThenBy(x => x.Name).ToArray();
        decimal Sum(ObligationType type) => ordered.Where(x => x.Type == type).Sum(x => x.Amount);
        var loansTotal = Sum(ObligationType.Loan);
        var cardTotal = Sum(ObligationType.CreditCard);
        var temporaryTotal = Sum(ObligationType.TemporaryPayment);
        var plannedTotal = Sum(ObligationType.PlannedInstallment);
        var total = ordered.Where(x => !x.IsEstimate).Sum(x => x.Amount);
        var projectedTotal = ordered.Sum(x => x.Amount);
        return new MandatoryPaymentSummary(
            ordered,
            loansTotal,
            cardTotal,
            temporaryTotal,
            plannedTotal,
            emergencyFundContribution,
            total,
            projectedTotal);
    }
}
