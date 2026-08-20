namespace CoinFlow.Domain.Calculations;

public sealed class TargetAmountCalculator
{
    public SalaryPeriodProjection? FindFirstReached(
        IEnumerable<SalaryPeriodProjection> projections,
        decimal targetAmount)
    {
        if (targetAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetAmount),
                "Hedef tutar negatif olamaz.");
        }

        return projections
            .OrderBy(x => x.PeriodStart)
            .FirstOrDefault(x => x.EndingProjectedSavings >= targetAmount);
    }
}

