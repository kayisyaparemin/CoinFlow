namespace CoinFlow.Domain.Calculations;

public sealed class InstallmentScheduleCalculator
{
    public IReadOnlyList<(DateOnly Date, decimal Amount)> Split(
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

        var regular = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var paidBeforeLast = regular * (count - 1);
        return Enumerable.Range(0, count)
            .Select(index => (
                firstDate.AddMonths(index),
                index == count - 1 ? total - paidBeforeLast : regular))
            .ToArray();
    }
}
