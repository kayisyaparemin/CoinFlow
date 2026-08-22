using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class HistoricalPlanRevisionService(
    ICoinFlowStore store,
    IClock clock,
    PeriodPlanSnapshotService planSnapshotService)
{
    public async Task<PeriodPlanRevision?> CaptureOpenPlanRevisionAsync(
        FinancialPlan currentPlan,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        if (!CanBuildProjection(currentPlan))
        {
            return null;
        }

        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        if (currentSnapshot is null)
        {
            return null;
        }

        var openPlan = history.Plans
            .Where(x => x.FinancialSnapshotId == currentSnapshot.Id)
            .Where(x => history.Actuals.All(actual =>
                actual.PeriodPlanSnapshotId != x.Id))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (openPlan is null ||
            clock.Today > openPlan.ReviewAvailableFrom)
        {
            return null;
        }

        var scopedPlan = currentPlan with
        {
            Settings = currentPlan.Settings with
            {
                ProjectionStartingSavings =
                    currentSnapshot.ProjectionStartingSavings,
                ProjectionAnchorDate = currentSnapshot.ProjectionAnchorDate,
                SalaryDay = currentSnapshot.SalaryDay
            }
        };
        var latestFrozenPlan = planSnapshotService.Freeze(
            scopedPlan,
            currentSnapshot,
            clock.UtcNow);
        if (latestFrozenPlan.PeriodStart != openPlan.PeriodStart ||
            latestFrozenPlan.PeriodEnd != openPlan.PeriodEnd)
        {
            return null;
        }

        var revisions = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == openPlan.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.RevisionNumber)
            .ToArray();
        var currentFrozen = revisions.Length == 0
            ? FrozenPlanSignature.From(openPlan)
            : FrozenPlanSignature.From(revisions[^1]);
        var candidate = FrozenPlanSignature.From(latestFrozenPlan);
        if (currentFrozen == candidate)
        {
            return null;
        }

        // Current/future screens derive on demand. This persisted value is
        // history-only: "what did the plan say after this planning change?"
        var revision = ToRevision(
            openPlan.Id,
            latestFrozenPlan,
            revisions.Length + 1,
            clock.UtcNow,
            trigger);
        await store.SavePeriodPlanRevisionAsync(
            revision,
            cancellationToken);
        return revision;
    }

    private static PeriodPlanRevision ToRevision(
        Guid periodPlanSnapshotId,
        PeriodPlanSnapshot frozen,
        int revisionNumber,
        DateTimeOffset createdAtUtc,
        string trigger)
    {
        var revisionId = Guid.NewGuid();
        return new PeriodPlanRevision
        {
            Id = revisionId,
            PeriodPlanSnapshotId = periodPlanSnapshotId,
            RevisionNumber = revisionNumber,
            CreatedAtUtc = createdAtUtc,
            Trigger = trigger.Trim(),
            StrategyUsed = frozen.StrategyUsed,
            PlannedIncome = frozen.PlannedIncome,
            PlannedLoanPayments = frozen.PlannedLoanPayments,
            PlannedCardPayments = frozen.PlannedCardPayments,
            PlannedTemporaryPayments = frozen.PlannedTemporaryPayments,
            PlannedInstallmentPayments = frozen.PlannedInstallmentPayments,
            PlannedOtherScheduledPayments =
                frozen.PlannedOtherScheduledPayments,
            PlannedMandatoryPayments = frozen.PlannedMandatoryPayments,
            PlannedLivingBudget = frozen.PlannedLivingBudget,
            PlannedLargeExpenses = frozen.PlannedLargeExpenses,
            PlannedCardInterest = frozen.PlannedCardInterest,
            PlannedDeficitInterest = frozen.PlannedDeficitInterest,
            PlannedInterest = frozen.PlannedCardInterest +
                              frozen.PlannedDeficitInterest,
            PlannedEndingSavings = frozen.PlannedEndingSavings,
            Note = trigger.Trim(),
            PaymentLines = frozen.PaymentLines
                .Select(line => line with
                {
                    Id = Guid.NewGuid(),
                    PeriodPlanSnapshotId = periodPlanSnapshotId
                })
                .ToArray()
        };
    }

    private static bool CanBuildProjection(FinancialPlan plan) =>
        plan.Salaries.Count > 0 &&
        plan.PaymentAssignmentStrategies.Count > 0 &&
        plan.Settings.ProjectionAnchorDate != default;

    private sealed record FrozenPlanSignature(
        PaymentAssignmentMode StrategyUsed,
        decimal PlannedIncome,
        decimal PlannedLoanPayments,
        decimal PlannedCardPayments,
        decimal PlannedTemporaryPayments,
        decimal PlannedInstallmentPayments,
        decimal PlannedOtherScheduledPayments,
        decimal PlannedMandatoryPayments,
        decimal PlannedLivingBudget,
        decimal PlannedLargeExpenses,
        decimal PlannedCardInterest,
        decimal PlannedDeficitInterest,
        decimal PlannedEndingSavings,
        IReadOnlyList<FrozenPaymentLineSignature> Lines)
    {
        public static FrozenPlanSignature From(PeriodPlanSnapshot plan) => new(
            plan.StrategyUsed,
            plan.PlannedIncome,
            plan.PlannedLoanPayments,
            plan.PlannedCardPayments,
            plan.PlannedTemporaryPayments,
            plan.PlannedInstallmentPayments,
            plan.PlannedOtherScheduledPayments,
            plan.PlannedMandatoryPayments,
            plan.PlannedLivingBudget,
            plan.PlannedLargeExpenses,
            plan.PlannedCardInterest,
            plan.PlannedDeficitInterest,
            plan.PlannedEndingSavings,
            plan.PaymentLines
                .Select(FrozenPaymentLineSignature.From)
                .OrderBy(x => x.PlannedDate)
                .ThenBy(x => x.SourceType)
                .ThenBy(x => x.SourceEntityId)
                .ToArray());

        public static FrozenPlanSignature From(PeriodPlanRevision revision) =>
            new(
                revision.StrategyUsed,
                revision.PlannedIncome,
                revision.PlannedLoanPayments,
                revision.PlannedCardPayments,
                revision.PlannedTemporaryPayments,
                revision.PlannedInstallmentPayments,
                revision.PlannedOtherScheduledPayments,
                revision.PlannedMandatoryPayments,
                revision.PlannedLivingBudget,
                revision.PlannedLargeExpenses,
                revision.PlannedCardInterest,
                revision.PlannedDeficitInterest,
                revision.PlannedEndingSavings,
                revision.PaymentLines
                    .Select(FrozenPaymentLineSignature.From)
                    .OrderBy(x => x.PlannedDate)
                    .ThenBy(x => x.SourceType)
                    .ThenBy(x => x.SourceEntityId)
                    .ToArray());

        public bool Equals(FrozenPlanSignature? other) =>
            other is not null &&
            StrategyUsed == other.StrategyUsed &&
            PlannedIncome == other.PlannedIncome &&
            PlannedLoanPayments == other.PlannedLoanPayments &&
            PlannedCardPayments == other.PlannedCardPayments &&
            PlannedTemporaryPayments == other.PlannedTemporaryPayments &&
            PlannedInstallmentPayments == other.PlannedInstallmentPayments &&
            PlannedOtherScheduledPayments ==
            other.PlannedOtherScheduledPayments &&
            PlannedMandatoryPayments == other.PlannedMandatoryPayments &&
            PlannedLivingBudget == other.PlannedLivingBudget &&
            PlannedLargeExpenses == other.PlannedLargeExpenses &&
            PlannedCardInterest == other.PlannedCardInterest &&
            PlannedDeficitInterest == other.PlannedDeficitInterest &&
            PlannedEndingSavings == other.PlannedEndingSavings &&
            Lines.SequenceEqual(other.Lines);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(StrategyUsed);
            hash.Add(PlannedIncome);
            hash.Add(PlannedLoanPayments);
            hash.Add(PlannedCardPayments);
            hash.Add(PlannedTemporaryPayments);
            hash.Add(PlannedInstallmentPayments);
            hash.Add(PlannedOtherScheduledPayments);
            hash.Add(PlannedMandatoryPayments);
            hash.Add(PlannedLivingBudget);
            hash.Add(PlannedLargeExpenses);
            hash.Add(PlannedCardInterest);
            hash.Add(PlannedDeficitInterest);
            hash.Add(PlannedEndingSavings);
            foreach (var line in Lines)
            {
                hash.Add(line);
            }

            return hash.ToHashCode();
        }
    }

    private sealed record FrozenPaymentLineSignature(
        Guid SourceEntityId,
        PlanPaymentSourceType SourceType,
        string Name,
        DateOnly PlannedDate,
        decimal? PlannedAmount,
        bool IsEstimate,
        string Detail)
    {
        public static FrozenPaymentLineSignature From(
            PeriodPlanPaymentLine line) => new(
            line.SourceEntityId,
            line.SourceType,
            line.Name,
            line.PlannedDate,
            line.PlannedAmount,
            line.IsEstimate,
            line.Detail);
    }
}
