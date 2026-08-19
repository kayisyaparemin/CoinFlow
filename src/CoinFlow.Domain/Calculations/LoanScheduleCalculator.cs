using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class LoanScheduleCalculator
{
    public IReadOnlyList<DateOnly> GetPaymentDates(Loan loan)
    {
        CalendarRules.ValidateDay(loan.PaymentDay);
        if (loan.MonthlyInstallment < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(loan), "Kredi taksiti negatif olamaz.");
        }

        var first = CalendarRules.ResolveDay(loan.StartDate.Year, loan.StartDate.Month, loan.PaymentDay);
        if (first < loan.StartDate)
        {
            first = CalendarRules.AddMonthsKeepingDay(first, 1, loan.PaymentDay);
        }

        if (loan.InstallmentCount is > 0)
        {
            return Enumerable.Range(0, loan.InstallmentCount.Value)
                .Select(i => CalendarRules.AddMonthsKeepingDay(first, i, loan.PaymentDay))
                .Where(x => loan.EndDate is null || x <= loan.EndDate.Value)
                .ToArray();
        }

        if (loan.EndDate is null || loan.EndDate < first)
        {
            return [];
        }

        var dates = new List<DateOnly>();
        for (var date = first; date <= loan.EndDate; date = CalendarRules.AddMonthsKeepingDay(date, 1, loan.PaymentDay))
        {
            dates.Add(date);
        }

        return dates;
    }
}
