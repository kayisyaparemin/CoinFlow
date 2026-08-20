using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class LoanScheduleCalculator
{
    public IReadOnlyList<DateOnly> GetPaymentDates(Loan loan)
    {
        CalendarRules.ValidateDay(loan.PaymentDay);
        if (loan.MonthlyPayment <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(loan), "Kredi taksiti sıfırdan büyük olmalıdır.");
        }

        if (loan.NextPaymentDate == default)
        {
            throw new InvalidOperationException("Kredinin exact ilk/sonraki ödeme tarihi gereklidir.");
        }

        if (loan.RemainingInstallmentCount < 1)
        {
            return [];
        }

        return Enumerable.Range(0, loan.RemainingInstallmentCount)
            .Select(index => index == 0
                ? loan.NextPaymentDate
                : CalendarRules.AddMonthsKeepingDay(
                    loan.NextPaymentDate,
                    index,
                    loan.PaymentDay))
            .ToArray();
    }
}
