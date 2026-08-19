using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum CreditCardPaymentResolution
{
    Undetermined = 0,
    DueDateOverride = 1,
    GeneralStrategy = 2,
    ProjectionFallback = 3
}

public sealed record CreditCardStatementProjection(
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? OpeningCarriedBalance,
    decimal NewCharges,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? Payment,
    decimal? CarriedAfterPayment,
    CreditCardPaymentResolution PaymentResolution,
    CreditCardPaymentType? AppliedPaymentType)
{
    public bool IsPaymentDetermined => Payment is not null;
    public bool UsesProjectionFallback => PaymentResolution == CreditCardPaymentResolution.ProjectionFallback;
}

public sealed class CreditCardProjectionCalculator
{
    public static decimal DeriveCurrentTotalDebt(CreditCard card)
    {
        ValidateMoney(card);
        return card.CarriedBalance + card.UnbilledSpending + card.Charges.Sum(x => x.Amount);
    }

    public IReadOnlyList<CreditCardStatementProjection> Project(
        CreditCard card,
        int statementCount,
        bool useProjectionFallback = false)
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
        decimal? carried = card.CarriedBalance;
        var result = new List<CreditCardStatementProjection>(statementCount);

        for (var index = 0; index < statementCount; index++)
        {
            var charges = assignedCharges.GetValueOrDefault(closeDate);
            if (index == 0)
            {
                charges += card.UnbilledSpending;
            }

            decimal? statementBalance = carried is null ? null : carried.Value + charges;
            decimal? minimumPayment = statementBalance is null
                ? null
                : RoundPayment(statementBalance.Value * card.MinimumPaymentRate);
            var dueDate = ResolvePaymentDueDate(closeDate, card.PaymentDueDay);
            var decision = ResolvePayment(
                card,
                dueDate,
                statementBalance,
                minimumPayment,
                useProjectionFallback);
            decimal? carriedAfterPayment = statementBalance is null || decision.Payment is null
                ? null
                : Math.Max(0m, statementBalance.Value - decision.Payment.Value);

            result.Add(new CreditCardStatementProjection(
                closeDate,
                dueDate,
                carried,
                charges,
                statementBalance,
                minimumPayment,
                decision.Payment,
                carriedAfterPayment,
                decision.Resolution,
                decision.PaymentType));

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

    private static PaymentDecision ResolvePayment(
        CreditCard card,
        DateOnly dueDate,
        decimal? statementBalance,
        decimal? minimumPayment,
        bool useProjectionFallback)
    {
        var paymentOverride = card.PaymentPlans.FirstOrDefault(x => x.DueDate == dueDate);
        if (paymentOverride is not null)
        {
            return new PaymentDecision(
                CalculatePayment(
                    paymentOverride.PaymentType,
                    paymentOverride.Amount,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.DueDateOverride,
                paymentOverride.PaymentType);
        }

        var strategyType = ToPaymentType(card.PaymentStrategy);
        if (strategyType is not null)
        {
            return new PaymentDecision(
                CalculatePayment(strategyType.Value, card.FixedPaymentAmount, statementBalance, minimumPayment),
                CreditCardPaymentResolution.GeneralStrategy,
                strategyType);
        }

        var fallbackType = useProjectionFallback
            ? ToPaymentType(card.ProjectionFallbackStrategy)
            : null;
        if (fallbackType is not null)
        {
            return new PaymentDecision(
                CalculatePayment(
                    fallbackType.Value,
                    card.ProjectionFallbackFixedAmount,
                    statementBalance,
                    minimumPayment),
                CreditCardPaymentResolution.ProjectionFallback,
                fallbackType);
        }

        return new PaymentDecision(null, CreditCardPaymentResolution.Undetermined, null);
    }

    private static decimal? CalculatePayment(
        CreditCardPaymentType paymentType,
        decimal? fixedAmount,
        decimal? statementBalance,
        decimal? minimumPayment)
    {
        if (statementBalance is null || minimumPayment is null)
        {
            return null;
        }

        var requested = paymentType switch
        {
            CreditCardPaymentType.Minimum => minimumPayment.Value,
            CreditCardPaymentType.FullStatement => statementBalance.Value,
            CreditCardPaymentType.FixedAmount => Math.Max(
                fixedAmount ?? throw new InvalidOperationException("Sabit kart ödeme tutarı gereklidir."),
                minimumPayment.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(paymentType))
        };
        return Math.Min(statementBalance.Value, Math.Max(0m, RoundPayment(requested)));
    }

    private static CreditCardPaymentType? ToPaymentType(CreditCardPaymentStrategy strategy) => strategy switch
    {
        CreditCardPaymentStrategy.AskEachStatement => null,
        CreditCardPaymentStrategy.Minimum => CreditCardPaymentType.Minimum,
        CreditCardPaymentStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        CreditCardPaymentStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static CreditCardPaymentType? ToPaymentType(ProjectionFallbackStrategy strategy) => strategy switch
    {
        ProjectionFallbackStrategy.None => null,
        ProjectionFallbackStrategy.Minimum => CreditCardPaymentType.Minimum,
        ProjectionFallbackStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        ProjectionFallbackStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static decimal RoundPayment(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void Validate(CreditCard card)
    {
        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.MinimumPaymentRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(card), "Asgari ödeme oranı 0 ile 1 arasında olmalıdır.");
        }

        ValidateMoney(card);
        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException("Sabit ödeme stratejisi için pozitif tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy == ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException("Sabit projeksiyon varsayımı için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount && x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException("Özel kart ödemesi için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.GroupBy(x => x.DueDate).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException("Aynı son ödeme tarihi için yalnızca bir özel kart planı olabilir.");
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

    private sealed record PaymentDecision(
        decimal? Payment,
        CreditCardPaymentResolution Resolution,
        CreditCardPaymentType? PaymentType);
}
