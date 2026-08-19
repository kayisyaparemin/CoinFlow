using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record CreditCardStatementProjection(
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal OpeningCarriedBalance,
    decimal NewCharges,
    decimal StatementBalance,
    decimal Payment,
    decimal CarriedAfterPayment);

public sealed class CreditCardProjectionCalculator
{
    public static decimal DeriveCurrentTotalDebt(CreditCard card)
    {
        ValidateMoney(card);
        return card.CarriedBalance + card.UnbilledSpending + card.Charges.Sum(x => x.Amount);
    }

    public IReadOnlyList<CreditCardStatementProjection> Project(CreditCard card, int statementCount)
    {
        if (statementCount < 1)
        {
            return [];
        }

        Validate(card);
        var anchor = card.BalanceAsOfDate == default
            ? throw new InvalidOperationException("Kart bakiye referans tarihi gereklidir.")
            : card.BalanceAsOfDate;
        var closeDate = ResolveStatementCloseOnOrAfter(anchor, card.StatementClosingDay);
        var firstClose = closeDate;
        var assignedCharges = card.Charges
            .GroupBy(x => ResolveChargeStatementClose(x.PostingDate, firstClose, card.StatementClosingDay))
            .ToDictionary(x => x.Key, x => x.Sum(charge => charge.Amount));
        var carried = card.CarriedBalance;
        var result = new List<CreditCardStatementProjection>(statementCount);

        for (var index = 0; index < statementCount; index++)
        {
            var charges = assignedCharges.GetValueOrDefault(closeDate);
            if (index == 0)
            {
                charges += card.UnbilledSpending;
            }

            var statementBalance = carried + charges;
            var dueDate = ResolvePaymentDueDate(closeDate, card.PaymentDueDay);
            var manual = card.PaymentPlans
                .Where(x => x.DueDate == dueDate)
                .OrderByDescending(x => x.PlannedPaymentAmount)
                .FirstOrDefault();
            var payment = manual is null
                ? decimal.Round(statementBalance * card.MinimumPaymentRate, 2, MidpointRounding.AwayFromZero)
                : manual.PlannedPaymentAmount;
            payment = Math.Min(statementBalance, Math.Max(0m, payment));
            var carriedAfterPayment = Math.Max(0m, statementBalance - payment);

            result.Add(new CreditCardStatementProjection(
                closeDate,
                dueDate,
                carried,
                charges,
                statementBalance,
                payment,
                carriedAfterPayment));

            carried = carriedAfterPayment;
            closeDate = CalendarRules.AddMonthsKeepingDay(closeDate, 1, card.StatementClosingDay);
        }

        return result;
    }

    public static DateOnly ResolveStatementCloseOnOrAfter(DateOnly date, int closingDay)
    {
        var close = CalendarRules.ResolveDay(date.Year, date.Month, closingDay);
        return close >= date
            ? close
            : CalendarRules.AddMonthsKeepingDay(close, 1, closingDay);
    }

    public static DateOnly ResolveChargeStatementClose(
        DateOnly postingDate,
        DateOnly firstProjectionClose,
        int closingDay)
    {
        var close = ResolveStatementCloseOnOrAfter(postingDate, closingDay);
        return close < firstProjectionClose ? firstProjectionClose : close;
    }

    public static DateOnly ResolvePaymentDueDate(DateOnly statementCloseDate, int paymentDueDay)
    {
        CalendarRules.ValidateDay(paymentDueDay);
        var sameMonth = CalendarRules.ResolveDay(
            statementCloseDate.Year,
            statementCloseDate.Month,
            paymentDueDay);
        return sameMonth > statementCloseDate
            ? sameMonth
            : CalendarRules.AddMonthsKeepingDay(sameMonth, 1, paymentDueDay);
    }

    private static void Validate(CreditCard card)
    {
        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.MinimumPaymentRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(card), "Asgari ödeme oranı 0 ile 1 arasında olmalıdır.");
        }

        ValidateMoney(card);
        if (card.PaymentPlans.Any(x => x.PlannedPaymentAmount < 0m))
        {
            throw new InvalidOperationException("Manuel kart ödemesi negatif olamaz.");
        }
    }

    private static void ValidateMoney(CreditCard card)
    {
        if (card.CarriedBalance < 0m ||
            card.UnbilledSpending < 0m ||
            card.Charges.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException("Kart borç bileşenleri negatif olamaz.");
        }
    }
}
