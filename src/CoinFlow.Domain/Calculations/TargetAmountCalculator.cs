namespace CoinFlow.Domain.Calculations;

public sealed record TargetReachabilityResult(
    bool IsAlreadyReached,
    SalaryPeriodProjection? FirstReachedPeriod)
{
    public bool IsReached => IsAlreadyReached || FirstReachedPeriod is not null;
}

public sealed class TargetAmountCalculator
{
    public SalaryPeriodProjection? FindFirstReached(
        IEnumerable<SalaryPeriodProjection> projections,
        decimal targetAmount)
    {
        ValidateTarget(targetAmount);

        return projections
            .OrderBy(x => x.PeriodStart)
            .FirstOrDefault(x => x.EndingProjectedSavings >= targetAmount);
    }

    public TargetReachabilityResult FindFirstReachable(
        IEnumerable<SalaryPeriodProjection> projections,
        decimal targetAmount)
    {
        ValidateTarget(targetAmount);
        var ordered = projections
            .OrderBy(x => x.PeriodStart)
            .ToArray();
        if (ordered.FirstOrDefault()?.OpeningProjectedSavings >= targetAmount)
        {
            return new TargetReachabilityResult(true, null);
        }

        return new TargetReachabilityResult(
            false,
            ordered.FirstOrDefault(x =>
                x.EndingProjectedSavings >= targetAmount));
    }

    private static void ValidateTarget(decimal targetAmount)
    {
        if (targetAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetAmount),
                "Hedef tutar 0'dan büyük olmalıdır.");
        }
    }
}
