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
    decimal CarryInterest,
    decimal? NextCarriedBalance,
    decimal AppliedInterestRate,
    CreditCardPaymentResolution PaymentResolution,
    CreditCardPaymentType? AppliedPaymentType)
{
    public bool IsPaymentDetermined => Payment is not null;
    public bool UsesProjectionFallback =>
        PaymentResolution == CreditCardPaymentResolution.ProjectionFallback;
}

public sealed class CreditCardStatementCalculator
{
    public IReadOnlyList<CreditCardStatementProjection> Project(
        CreditCard card,
        int statementCount,
        bool useProjectionFallback = false,
        decimal carryInterestRate = 0.05m)
    {
        if (statementCount < 1)
        {
            return [];
        }

        Validate(card);
        ValidateInterestRate(carryInterestRate);
        var firstClose = ResolveStatementCloseOnOrAfter(
            card.BalanceAsOfDate,
            card.StatementClosingDay);
        var closeDate = firstClose;
        var assignedCharges = card.Charges
            .GroupBy(x => ResolveChargeStatementClose(
                x.PostingDate,
                firstClose,
                card.StatementClosingDay))
            .ToDictionary(x => x.Key, x => x.Sum(charge => charge.Amount));
        decimal? carried = card.CarriedBalance;
        var result = new List<CreditCardStatementProjection>(statementCount);

        for (var index = 0; index < statementCount; index++)
        {
            var newCharges = assignedCharges.GetValueOrDefault(closeDate);
            if (index == 0)
            {
                newCharges += card.UnbilledSpending;
            }

            decimal? statementBalance = carried is null
                ? null
                : carried.Value + newCharges;
            decimal? minimumPayment = statementBalance is null
                ? null
                : RoundMoney(statementBalance.Value * card.MinimumPaymentRate);
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
            var carryInterest = carriedAfterPayment is > 0m
                ? RoundMoney(carriedAfterPayment.Value * carryInterestRate)
                : 0m;
            decimal? nextCarriedBalance = carriedAfterPayment is null
                ? null
                : carriedAfterPayment.Value + carryInterest;

            result.Add(new CreditCardStatementProjection(
                closeDate,
                dueDate,
                carried,
                newCharges,
                statementBalance,
                minimumPayment,
                decision.Payment,
                carriedAfterPayment,
                carryInterest,
                nextCarriedBalance,
                carryInterestRate,
                decision.Resolution,
                decision.PaymentType));

            carried = nextCarriedBalance;
            closeDate = CalendarRules.AddMonthsKeepingDay(
                closeDate,
                1,
                card.StatementClosingDay);
        }

        return result;
    }

    public static DateOnly ResolveStatementCloseOnOrAfter(
        DateOnly date,
        int statementClosingDay)
    {
        CalendarRules.ValidateDay(statementClosingDay);
        var closeDate = CalendarRules.ResolveDay(date.Year, date.Month, statementClosingDay);
        return closeDate >= date
            ? closeDate
            : CalendarRules.AddMonthsKeepingDay(closeDate, 1, statementClosingDay);
    }

    public static DateOnly ResolveChargeStatementClose(
        DateOnly postingDate,
        DateOnly firstProjectionClose,
        int statementClosingDay)
    {
        var closeDate = ResolveStatementCloseOnOrAfter(postingDate, statementClosingDay);
        return closeDate < firstProjectionClose ? firstProjectionClose : closeDate;
    }

    public static DateOnly ResolvePaymentDueDate(
        DateOnly statementCloseDate,
        int paymentDueDay)
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
        var paymentOverride = card.PaymentPlans.SingleOrDefault(x => x.DueDate == dueDate);
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
                CalculatePayment(
                    strategyType.Value,
                    card.FixedPaymentAmount,
                    statementBalance,
                    minimumPayment),
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

        return new PaymentDecision(
            null,
            CreditCardPaymentResolution.Undetermined,
            null);
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
                fixedAmount ?? throw new InvalidOperationException(
                    "Sabit kart ödeme tutarı gereklidir."),
                minimumPayment.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(paymentType))
        };

        return Math.Min(
            statementBalance.Value,
            Math.Max(0m, RoundMoney(requested)));
    }

    private static CreditCardPaymentType? ToPaymentType(
        CreditCardPaymentStrategy strategy) => strategy switch
    {
        CreditCardPaymentStrategy.AskEachStatement => null,
        CreditCardPaymentStrategy.Minimum => CreditCardPaymentType.Minimum,
        CreditCardPaymentStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        CreditCardPaymentStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static CreditCardPaymentType? ToPaymentType(
        ProjectionFallbackStrategy strategy) => strategy switch
    {
        ProjectionFallbackStrategy.None => null,
        ProjectionFallbackStrategy.Minimum => CreditCardPaymentType.Minimum,
        ProjectionFallbackStrategy.FullStatement => CreditCardPaymentType.FullStatement,
        ProjectionFallbackStrategy.FixedAmount => CreditCardPaymentType.FixedAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void ValidateInterestRate(decimal rate)
    {
        if (rate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                "Kart devreden borç faiz oranı 0 ile 1 arasında olmalıdır.");
        }
    }

    private static void Validate(CreditCard card)
    {
        if (card.BalanceAsOfDate == default)
        {
            throw new InvalidOperationException("Kart bakiye referans tarihi gereklidir.");
        }

        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.MinimumPaymentRate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(card),
                "Asgari ödeme oranı 0 ile 1 arasında olmalıdır.");
        }

        if (card.CarriedBalance < 0m ||
            card.UnbilledSpending < 0m ||
            card.Charges.Any(x => x.Amount < 0m))
        {
            throw new InvalidOperationException(
                "Kart borç bileşenleri negatif olamaz.");
        }

        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit ödeme stratejisi için pozitif tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy == ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit projeksiyon varsayımı için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount &&
                x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException(
                "Özel kart ödemesi için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.GroupBy(x => x.DueDate).Any(x => x.Count() > 1))
        {
            throw new InvalidOperationException(
                "Aynı son ödeme tarihi için yalnızca bir özel kart planı olabilir.");
        }
    }

    private sealed record PaymentDecision(
        decimal? Payment,
        CreditCardPaymentResolution Resolution,
        CreditCardPaymentType? PaymentType);
}
