namespace CoinFlow.Domain.Calculations;

public sealed record ScheduledAmount(DateOnly Date, decimal Amount);

public sealed class InstallmentScheduleCalculator
{
    public IReadOnlyList<ScheduledAmount> Split(
        decimal total,
        int count,
        DateOnly firstDate)
    {
        if (total <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "Toplam tutar sıfırdan büyük olmalıdır.");
        }

        if (count is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Taksit sayısı 1 ile 120 arasında olmalıdır.");
        }

        if (firstDate == default)
        {
            throw new ArgumentException("İlk ödeme tarihi gereklidir.", nameof(firstDate));
        }

        var regular = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var paidBeforeLast = regular * (count - 1);
        return Enumerable.Range(0, count)
            .Select(index => new ScheduledAmount(
                CalendarRules.AddMonthsKeepingDay(firstDate, index, firstDate.Day),
                index == count - 1 ? total - paidBeforeLast : regular))
            .ToArray();
    }
}
