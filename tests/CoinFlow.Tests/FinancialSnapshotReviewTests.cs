using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

public sealed class FinancialSnapshotReviewTests
{
    private static readonly DateOnly InitialDate = new(2026, 8, 20);
    private static readonly DateOnly ReviewDate = new(2026, 10, 10);

    [Fact]
    public async Task FirstUse_CreatesCurrentSnapshotAndFrozenPlan_WithoutHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            var financialPlan = await service.GetFinancialPlanAsync();
            var history = await store.GetFinancialHistoryAsync();

            var snapshot = Assert.Single(history.Snapshots);
            var frozen = Assert.Single(history.Plans);
            Assert.True(snapshot.IsCurrent);
            Assert.Equal(InitialDate, snapshot.SnapshotDate);
            Assert.Equal(new DateOnly(2026, 10, 10),
                snapshot.NextReviewDate);
            Assert.Equal(snapshot.Id, frozen.FinancialSnapshotId);
            Assert.Empty(history.Actuals);
            Assert.Empty(await service.GetHistoryPeriodsAsync());

            var projected = Assert.Single(
                TestFactory.ProjectionCalculator().Calculate(
                    financialPlan,
                    InitialDate,
                    1));
            Assert.Equal(projected.EndingProjectedSavings,
                frozen.PlannedEndingSavings);
            Assert.Equal(projected.MandatoryOutflow,
                frozen.PlannedMandatoryPayments);
            Assert.Equal(projected.PaymentAssignmentMode,
                frozen.StrategyUsed);
        });
    }

    [Fact]
    public async Task Review_FinalizesAtomically_PersistsAcrossRestart_AndRefreshesBaseline()
    {
        var path = TempPath();
        try
        {
            Guid oldSnapshotId;
            decimal confirmed;
            await using (var first = NewStore(path))
            {
                var initial = TestFactory.Service(first, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();
                oldSnapshotId = Assert.Single(
                    (await first.GetFinancialHistoryAsync()).Snapshots).Id;

                var review = TestFactory.Service(first, ReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                var draft = DefaultDraft(context, 37_500m);
                var preview = await review.PreviewPeriodReviewAsync(draft);
                confirmed = preview.SuggestedStartingSavings;
                var result = await review.FinalizePeriodReviewAsync(
                    draft with { ConfirmedStartingSavings = confirmed });

                Assert.Equal(ReviewDate, result.NewSnapshot.SnapshotDate);
                Assert.Equal(oldSnapshotId,
                    result.NewSnapshot.PreviousSnapshotId);
                Assert.Equal(confirmed,
                    result.NewSnapshot.ProjectionStartingSavings);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    review.FinalizePeriodReviewAsync(draft));
            }

            await using (var restartedStore = NewStore(path))
            {
                var restarted = TestFactory.Service(
                    restartedStore,
                    ReviewDate);
                var plan = await restarted.GetFinancialPlanAsync();
                var history = await restartedStore
                    .GetFinancialHistoryAsync();
                var latest = Assert.Single(history.Snapshots, x =>
                    x.IsCurrent);

                Assert.Equal(ReviewDate, latest.SnapshotDate);
                Assert.Equal(confirmed,
                    plan.Settings.ProjectionStartingSavings);
                Assert.Equal(ReviewDate,
                    plan.Settings.ProjectionAnchorDate);
                Assert.Single(history.Actuals);
                Assert.Single(await restarted.GetHistoryPeriodsAsync());
                Assert.False((await restarted
                    .GetPeriodReviewAvailabilityAsync()).IsDue);
                Assert.Equal(
                    confirmed,
                    (await restarted.GetFuturePeriodsAsync(
                        periodCount: 1))[0].OpeningProjectedSavings);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task PlanRevisionAndFutureSettings_DoNotRewriteFrozenHistory()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, ReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var originalEnding = context.OriginalPlan.PlannedEndingSavings;
            var result = await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, 38_000m) with
                {
                    RevisedLivingBudget = 35_000m
                });

            var currentSettings = (await review.GetFinancialPlanAsync())
                .Settings;
            await review.SaveSettingsAsync(currentSettings with
            {
                MonthlyLivingBudget = 40_000m,
                CreditCardCarryInterestRate = 0.04m
            });

            var period = Assert.Single(
                await review.GetHistoryPeriodsAsync());
            Assert.Equal(30_000m,
                period.OriginalPlan.PlannedLivingBudget);
            Assert.Equal(35_000m,
                Assert.IsType<PeriodPlanRevision>(period.Revision)
                    .PlannedLivingBudget);
            Assert.Equal(38_000m, period.Actual.ActualLivingSpend);
            Assert.Equal(originalEnding,
                period.OriginalPlan.PlannedEndingSavings);
            Assert.Equal(40_000m,
                (await review.GetFuturePeriodsAsync(
                    periodCount: 1))[0].LivingBudget);
            Assert.Equal(result.NewSnapshot.ProjectionStartingSavings,
                (await review.GetFinancialPlanAsync())
                    .Settings.ProjectionStartingSavings);
        });
    }

    [Fact]
    public async Task ActualCardPayment_UpdatesCanonicalCarryExactlyOnce()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            var originalPlan = await initial.GetFinancialPlanAsync();
            var originalCard = Assert.Single(originalPlan.CreditCards);
            var review = TestFactory.Service(store, ReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var cardLines = context.OriginalPlan.PaymentLines
                .Where(x =>
                    x.SourceType == PlanPaymentSourceType.CreditCard)
                .OrderBy(x => x.PlannedDate)
                .ToArray();
            var cardLine = cardLines[0];
            var actualAmount = cardLine.PlannedAmount.GetValueOrDefault() +
                               10_000m;
            var reconciler = new CreditCardActualPaymentReconciler(
                new CreditCardStatementCalculator());
            var expectedCard = originalCard;
            foreach (var line in cardLines)
            {
                expectedCard = reconciler.Apply(
                    expectedCard,
                    line.PlannedDate,
                    line.Id == cardLine.Id
                        ? actualAmount
                        : line.PlannedAmount.GetValueOrDefault(),
                    originalPlan.Settings.CreditCardCarryInterestRate);
            }

            var draft = DefaultDraft(context, 30_000m);
            draft = draft with
            {
                Payments = draft.Payments.Select(x =>
                    x.PeriodPlanPaymentLineId == cardLine.Id
                        ? x with
                        {
                            Status = ActualPaymentStatus.DifferentAmount,
                            ActualAmount = actualAmount
                        }
                        : x).ToArray()
            };
            await review.FinalizePeriodReviewAsync(draft);

            var updated = Assert.Single(
                (await review.GetFinancialPlanAsync()).CreditCards);
            Assert.Equal(expectedCard.CarriedBalance,
                updated.CarriedBalance);
            Assert.DoesNotContain(updated.PaymentPlans, x =>
                x.DueDate == cardLine.PlannedDate);
            var history = Assert.Single(
                await review.GetHistoryPeriodsAsync());
            Assert.Equal(actualAmount,
                Assert.Single(history.Actual.Payments, x =>
                    x.PeriodPlanPaymentLineId == cardLine.Id)
                    .ActualAmount);
        });
    }

    [Fact]
    public async Task UnpaidLoan_RemainsOutstandingInFuturePlan()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, ReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var loanLine = context.OriginalPlan.PaymentLines.First(x =>
                x.SourceType == PlanPaymentSourceType.Loan);
            var before = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == loanLine.SourceEntityId);
            var draft = DefaultDraft(context, 30_000m);
            draft = draft with
            {
                Payments = draft.Payments.Select(x =>
                    x.PeriodPlanPaymentLineId == loanLine.Id
                        ? x with
                        {
                            Status = ActualPaymentStatus.Unpaid,
                            ActualAmount = 0m,
                            ActualPaymentDate = null
                        }
                        : x).ToArray()
            };
            await review.FinalizePeriodReviewAsync(draft);

            var after = (await review.GetFinancialPlanAsync()).Loans
                .Single(x => x.Id == loanLine.SourceEntityId);
            Assert.Equal(before.RemainingInstallmentCount - 1,
                after.RemainingInstallmentCount);
            Assert.True(after.IsActive);
            Assert.Equal(ReviewDate, after.NextPaymentDate);
            Assert.Contains(
                (await review.GetFuturePeriodsAsync(periodCount: 1))[0]
                .MandatoryItems,
                x => x.PaymentId == after.Id);
        });
    }

    [Fact]
    public async Task PaidLoansAndScheduledPayments_AdvanceCanonicalState()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, ReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var before = await review.GetFinancialPlanAsync();

            await review.FinalizePeriodReviewAsync(
                DefaultDraft(context, 30_000m));
            var after = await review.GetFinancialPlanAsync();

            foreach (var loan in before.Loans)
            {
                var paidCount = context.OriginalPlan.PaymentLines.Count(x =>
                    x.SourceType == PlanPaymentSourceType.Loan &&
                    x.SourceEntityId == loan.Id);
                var updated = after.Loans.Single(x => x.Id == loan.Id);
                Assert.Equal(
                    loan.RemainingInstallmentCount - paidCount,
                    updated.RemainingInstallmentCount);
            }

            var scheduledLineIds = context.OriginalPlan.PaymentLines
                .Where(x => x.SourceType is
                    PlanPaymentSourceType.TemporaryPayment or
                    PlanPaymentSourceType.InstallmentPayment or
                    PlanPaymentSourceType.OtherScheduledPayment)
                .Select(x => x.SourceEntityId)
                .ToHashSet();
            Assert.All(
                after.PaymentPlans
                    .SelectMany(x => x.Installments)
                    .Where(x => scheduledLineIds.Contains(x.Id)),
                x => Assert.True(x.IsPaid));
        });
    }

    [Fact]
    public async Task SalaryDay31_SnapshotUsesCalendarResolvedReviewDate()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(
                store,
                new DateOnly(2027, 1, 31));
            await service.SaveSettingsAsync(new UserSettings
            {
                SalaryDay = 31,
                MonthlyLivingBudget = 10_000m,
                ProjectionStartingSavings = 5_000m,
                ProjectionAnchorDate = new DateOnly(2027, 1, 31)
            });
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                Amount = 50_000m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Maaş"
            });
            await service.CompleteInitialPaymentStrategySetupAsync(
                PaymentAssignmentMode.UpcomingPeriod);

            var snapshot = Assert.Single(
                (await store.GetFinancialHistoryAsync()).Snapshots);
            Assert.Equal(new DateOnly(2027, 2, 28),
                snapshot.NextReviewDate);
        });
    }

    [Fact]
    public async Task FirstInstallEquivalence_ProducesSameFutureState_WithHistoryOnlyForMonthlyUser()
    {
        var pathA = TempPath();
        var pathB = TempPath();
        try
        {
            FinancialPlan octoberState;
            IReadOnlyList<SalaryPeriodProjection> projectionA;
            await using (var storeA = NewStore(pathA))
            {
                var initial = TestFactory.Service(storeA, InitialDate);
                await initial.LoadCanonicalDevelopmentDataAsync();
                await initial.GetFinancialPlanAsync();
                var review = TestFactory.Service(storeA, ReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                await review.FinalizePeriodReviewAsync(
                    DefaultDraft(context, 37_500m));
                octoberState = await review.GetFinancialPlanAsync();
                projectionA = await review.GetFuturePeriodsAsync(
                    periodCount: 3);
                Assert.Single(await review.GetHistoryPeriodsAsync());
            }

            await using (var storeB = NewStore(pathB))
            {
                await CopyCanonicalStateAsync(octoberState, storeB);
                var fresh = TestFactory.Service(storeB, ReviewDate);
                var projectionB = await fresh.GetFuturePeriodsAsync(
                    periodCount: 3);

                Assert.Equal(
                    projectionA.Select(ProjectionSignature),
                    projectionB.Select(ProjectionSignature));
                Assert.Empty(await fresh.GetHistoryPeriodsAsync());
                Assert.Single(
                    (await storeB.GetFinancialHistoryAsync()).Snapshots);
            }
        }
        finally
        {
            DeleteDatabase(pathA);
            DeleteDatabase(pathB);
        }
    }

    [Fact]
    public async Task ClearData_RemovesSnapshotsAndHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            Assert.NotEmpty(
                (await store.GetFinancialHistoryAsync()).Snapshots);

            await service.ClearDevelopmentDataAsync();
            var history = await store.GetFinancialHistoryAsync();
            Assert.Empty(history.Snapshots);
            Assert.Empty(history.Plans);
            Assert.Empty(history.Revisions);
            Assert.Empty(history.Actuals);
        });
    }

    private static PeriodReviewDraft DefaultDraft(
        PeriodReviewContext context,
        decimal living) => new(
        context.OriginalPlan.Id,
        null,
        context.OriginalPlan.PaymentLines.Select(line =>
            new ActualPaymentDraft(
                line.Id,
                line.PlannedAmount is null
                    ? ActualPaymentStatus.Unpaid
                    : ActualPaymentStatus.Paid,
                line.PlannedAmount.GetValueOrDefault(),
                line.PlannedAmount is null ? null : line.PlannedDate))
            .ToArray(),
        living,
        context.OriginalPlan.PlannedDeficitInterest,
        [],
        [],
        null);

    private static object ProjectionSignature(
        SalaryPeriodProjection row) => new
        {
            row.PeriodStart,
            row.PeriodEnd,
            row.OpeningProjectedSavings,
            row.TotalIncome,
            row.MandatoryOutflow,
            row.LivingBudget,
            row.PlannedLargeCashExpenses,
            row.CardInterestGenerated,
            row.DeficitFinancingInterest,
            row.EndingProjectedSavings
        };

    private static async Task CopyCanonicalStateAsync(
        FinancialPlan source,
        ICoinFlowStore target)
    {
        await target.InitializeAsync();
        await target.SaveSettingsAsync(source.Settings);
        foreach (var item in source.Salaries)
            await target.UpsertSalaryAsync(item);
        foreach (var item in source.OtherIncomes)
            await target.UpsertOtherIncomeAsync(item);
        foreach (var item in source.Loans)
            await target.UpsertLoanAsync(item);
        foreach (var item in source.PaymentPlans)
            await target.UpsertPaymentPlanAsync(item);
        foreach (var item in source.CreditCards)
            await target.UpsertCreditCardAsync(item);
        foreach (var item in source.PlannedLargeExpenses)
            await target.UpsertPlannedLargeExpenseAsync(item);
        foreach (var item in source.PaymentAssignmentStrategies)
            await target.UpsertPaymentAssignmentStrategyAsync(item);
    }

    private static async Task WithStore(
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = TempPath();
        try
        {
            await using var store = NewStore(path);
            await test(store);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static SqliteCoinFlowStore NewStore(string path) =>
        new(path, true, InitialDate);

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-snapshot-{Guid.NewGuid():N}.db3");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[]
                 {
                     path,
                     path + "-shm",
                     path + "-wal"
                 })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
