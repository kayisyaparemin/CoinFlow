using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record PaymentAssignmentWindow(
    DateOnly SalaryDate,
    DateOnly StartInclusive,
    DateOnly EndInclusive);

public sealed class PaymentAssignmentResolver(
    SalaryPeriodCalculator salaryPeriodCalculator)
{
    public DateOnly ResolveFundingSalaryDate(
        DateOnly paymentDate,
        int salaryDay,
        PaymentAssignmentMode mode)
    {
        if (paymentDate == default)
        {
            throw new ArgumentException(
                "Gerçek ödeme tarihi gereklidir.",
                nameof(paymentDate));
        }

        ValidateMode(mode);
        var containingPeriod = salaryPeriodCalculator.GetPeriod(
            paymentDate,
            salaryDay);
        return mode switch
        {
            PaymentAssignmentMode.UpcomingPeriod => containingPeriod.Start,
            PaymentAssignmentMode.PreviousPeriod
                when paymentDate == containingPeriod.Start =>
                containingPeriod.Start,
            PaymentAssignmentMode.PreviousPeriod => containingPeriod.End,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    public PaymentAssignmentWindow ResolveWindow(
        DateOnly salaryDate,
        int salaryDay,
        PaymentAssignmentMode mode)
    {
        ValidateMode(mode);
        var salaryPeriod = salaryPeriodCalculator.GetPeriod(
            salaryDate,
            salaryDay);
        if (salaryPeriod.Start != salaryDate)
        {
            throw new ArgumentException(
                "Atama penceresi yalnızca gerçek bir maaş tarihi için çözülebilir.",
                nameof(salaryDate));
        }

        if (mode == PaymentAssignmentMode.UpcomingPeriod)
        {
            return new PaymentAssignmentWindow(
                salaryDate,
                salaryDate,
                salaryPeriod.End.AddDays(-1));
        }

        var previousSalaryDate = salaryPeriodCalculator
            .GetPeriod(salaryDate.AddDays(-1), salaryDay)
            .Start;
        return new PaymentAssignmentWindow(
            salaryDate,
            previousSalaryDate.AddDays(1),
            salaryDate);
    }

    private static void ValidateMode(PaymentAssignmentMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
