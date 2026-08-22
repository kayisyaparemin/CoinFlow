using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed record ProjectionBoundary(
    FinancialSnapshot Snapshot,
    DateOnly ProjectionAnchorDate,
    DateOnly FirstUnrealizedSalaryDate,
    decimal StartingSavings,
    DateOnly? ClosedCheckpointDate,
    Guid? SourcePeriodActualId)
{
    public bool IsActualFinalization => SourcePeriodActualId is not null;
}

public sealed class ProjectionBoundaryResolver(
    SalaryPeriodCalculator salaryPeriodCalculator)
{
    public ProjectionBoundary Resolve(
        FinancialHistoryData history,
        FinancialSnapshot currentSnapshot,
        UserSettings settings,
        DateOnly asOf)
    {
        var sourceActual = history.Actuals
            .Where(x => x.ResultFinancialSnapshotId == currentSnapshot.Id)
            .OrderByDescending(x => x.FinalizedAtUtc)
            .FirstOrDefault();
        var salaryDay = settings.SalaryDay;

        if (sourceActual is not null)
        {
            var firstUnrealizedSalary = salaryPeriodCalculator
                .GetFirstSalaryStrictlyAfter(
                    sourceActual.PeriodEnd,
                    salaryDay);
            return new ProjectionBoundary(
                currentSnapshot,
                sourceActual.PeriodEnd,
                firstUnrealizedSalary,
                currentSnapshot.ProjectionStartingSavings,
                sourceActual.PeriodEnd,
                sourceActual.Id);
        }

        var anchor = currentSnapshot.ProjectionAnchorDate == default
            ? asOf
            : currentSnapshot.ProjectionAnchorDate;
        var firstSalary = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
            anchor,
            salaryDay);
        return new ProjectionBoundary(
            currentSnapshot,
            anchor,
            firstSalary,
            currentSnapshot.ProjectionStartingSavings,
            null,
            null);
    }
}
