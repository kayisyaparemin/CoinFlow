using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record CreditCardMonthProjection(
    DateOnly Month,
    DateOnly PaymentDueDate,
    decimal OpeningStatementBalance,
    decimal NewCharges,
    decimal Payment,
    decimal ClosingBalance);

public sealed class CreditCardProjectionCalculator
{
    public static decimal DeriveCurrentTotalDebt(CreditCard card)
    {
        if (card.LastStatementRemaining < 0m ||
            card.CurrentCycleSpending < 0m ||
            card.FutureInstallments.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException("Kart borç bileşenleri negatif olamaz.");
        }

        return card.LastStatementRemaining +
               card.CurrentCycleSpending +
               card.FutureInstallments.Sum(x => x.Amount);
    }

    public IReadOnlyList<CreditCardMonthProjection> Project(
        CreditCard card,
        DateOnly firstMonth,
        int monthCount)
    {
        if (monthCount < 1)
        {
            return [];
        }

        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.MinimumPaymentRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(card), "Asgari ödeme oranı 0 ile 1 arasında olmalıdır.");
        }

        var month = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        var statementBalance = card.LastStatementRemaining > 0m
            ? card.LastStatementRemaining
            : card.LastStatementDebt;
        var result = new List<CreditCardMonthProjection>(monthCount);

        for (var index = 0; index < monthCount; index++)
        {
            var charges = card.FutureInstallments
                .Where(x => x.DueDate.Year == month.Year && x.DueDate.Month == month.Month)
                .Sum(x => x.Amount);
            if (index == 0)
            {
                charges += card.CurrentCycleSpending;
            }

            var payment = CalculatePayment(card, statementBalance, index == 0);
            var closing = Math.Max(0m, statementBalance - payment) + charges;
            var dueDate = CalendarRules.ResolveDay(month.Year, month.Month, card.PaymentDueDay);

            result.Add(new CreditCardMonthProjection(
                month,
                dueDate,
                statementBalance,
                charges,
                payment,
                closing));

            statementBalance = closing;
            month = month.AddMonths(1);
        }

        return result;
    }

    private static decimal CalculatePayment(CreditCard card, decimal balance, bool firstMonth)
    {
        if (balance <= 0m)
        {
            return 0m;
        }

        if (firstMonth && card.PaymentMode == CreditCardPaymentMode.Manual)
        {
            return Math.Min(balance, Math.Max(0m, card.ManualPaymentAmount ?? 0m));
        }

        return Math.Min(balance, decimal.Round(balance * card.MinimumPaymentRate, 2, MidpointRounding.AwayFromZero));
    }
}
