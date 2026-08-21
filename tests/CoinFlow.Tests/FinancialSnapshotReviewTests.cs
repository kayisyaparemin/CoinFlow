using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

public sealed class FinancialSnapshotReviewTests
{
    private static readonly DateOnly InitialDate = new(2026, 8, 20);
    private static readonly DateOnly FirstReviewDate = new(2026, 9, 10);
    private static readonly DateOnly SecondReviewDate = new(2026, 10, 10);

    [Fact]
    public async Task FirstUse_CreatesCurrentSnapshotAndFrozenPlan_WithoutHistory()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            var history = await store.GetFinancialHistoryAsync();

            var snapshot = Assert.Single(history.Snapshots);
            var frozen = Assert.Single(history.Plans);
            Assert.True(snapshot.IsCurrent);
            Assert.Equal(InitialDate, snapshot.SnapshotDate);
            Assert.Equal(FirstReviewDate, snapshot.NextReviewDate);
            Assert.Equal(snapshot.Id, frozen.FinancialSnapshotId);
            Assert.Equal(InitialDate, frozen.PeriodStart);
            Assert.Equal(FirstReviewDate, frozen.PeriodEnd);
            Assert.Equal(FirstReviewDate, frozen.ReviewAvailableFrom);
            Assert.Equal(20_322.58m, frozen.PlannedLivingBudget);
            Assert.All(frozen.PaymentLines, line =>
                Assert.True(
                    line.PlannedDate > InitialDate &&
                    line.PlannedDate <= FirstReviewDate));
            Assert.Contains(frozen.PaymentLines, x =>
                x.SourceType == PlanPaymentSourceType.CreditCard &&
                x.PlannedDate == new DateOnly(2026, 9, 5));
            Assert.Contains(frozen.PaymentLines, x =>
                x.SourceType == PlanPaymentSourceType.Loan &&
                x.PlannedDate == new DateOnly(2026, 9, 7));
            Assert.DoesNotContain(frozen.PaymentLines, x =>
                x.PlannedDate == new DateOnly(2026, 9, 18));
            Assert.Empty(history.Actuals);
            Assert.Empty(await service.GetHistoryPeriodsAsync());
            Assert.Equal(115_000m, frozen.PlannedIncome);
            Assert.Equal(
                frozen.OpeningSavings + frozen.PlannedIncome -
                frozen.PlannedMandatoryPayments -
                frozen.PlannedLivingBudget -
                frozen.PlannedLargeExpenses -
                frozen.PlannedDeficitInterest,
                frozen.PlannedEndingSavings);
        });
    }

    [Fact]
    public async Task ReviewAvailability_IsDueOnCheckpointAndRemainsDueAfterward()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            Assert.False((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(-1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);

            var onCheckpoint = await TestFactory.Service(
                store,
                FirstReviewDate).GetPeriodReviewAvailabilityAsync();
            Assert.True(onCheckpoint.IsDue);
            Assert.Equal(InitialDate, onCheckpoint.PendingPlan!.PeriodStart);
            Assert.Equal(FirstReviewDate, onCheckpoint.PendingPlan.PeriodEnd);

            Assert.True((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);
        });
    }

    [Fact]
    public void InitialReviewDate_DoesNotDependOnPaymentAssignmentMode()
    {
        var basePlan = TestFactory.CanonicalPlan();
        var snapshot = Snapshot(InitialDate);

        foreach (var mode in new[]
                 {
                     PaymentAssignmentMode.PreviousPeriod,
                     PaymentAssignmentMode.UpcomingPeriod
                 })
        {
            var plan = basePlan with
            {
                PaymentAssignmentStrategies =
                [basePlan.PaymentAssignmentStrategies[0] with { Mode = mode }]
            };
            var frozen = Freeze(plan, snapshot);

            Assert.Equal(InitialDate, frozen.PeriodStart);
            Assert.Equal(FirstReviewDate, frozen.PeriodEnd);
            Assert.Equal(FirstReviewDate, frozen.ReviewAvailableFrom);
        }
    }

    [Fact]
    public void InitialReviewPaymentWindow_IsSnapshotExclusiveAndReviewInclusive()
    {
        var planId = Guid.NewGuid();
        var dates = new[]
        {
            new DateOnly(2026, 8, 15),
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 5),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 18)
        };
        var plan = TestFactory.CanonicalPlan() with
        {
            Loans = [],
            CreditCards = [],
            PaymentPlans =
            [
                new TemporaryPaymentPlan
                {
                    Id = planId,
                    Name = "Sınır testi",
                    Installments = dates.Select((date, index) =>
                        new TemporaryPaymentInstallment
                        {
                            PlanId = planId,
                            DueDate = date,
                            Amount = 1_000m + index
                        }).ToArray()
                }
            ]
        };

        var frozen = Freeze(plan, Snapshot(InitialDate));

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 25),
                new DateOnly(2026, 9, 5),
                new DateOnly(2026, 9, 10)
            },
            frozen.PaymentLines.Select(x => x.PlannedDate));
    }

    [Fact]
    public void LivingBudget_IsProratedOnlyForInitialPartialPeriod()
    {
        var plan = TestFactory.CanonicalPlan();

        var partial = Freeze(plan, Snapshot(InitialDate));
        var fullDate = FirstReviewDate;
        var fullPlan = plan with
        {
            Settings = plan.Settings with
            {
                ProjectionAnchorDate = fullDate
            }
        };
        var full = Freeze(fullPlan, Snapshot(fullDate));

        Assert.Equal(20_322.58m, partial.PlannedLivingBudget);
        Assert.Equal(30_000m, full.PlannedLivingBudget);
    }

    [Fact]
    public async Task FirstInstallOnSalaryDate_StartsWithNextFullReviewOnly()
    {
        await WithStore(async store =>
        {
            var plan = TestFactory.CanonicalPlan();
            await CopyCanonicalStateAsync(
                plan with
                {
                    Settings = plan.Settings with
                    {
                        ProjectionAnchorDate = FirstReviewDate
                    }
                },
                store);

            var service = TestFactory.Service(store, FirstReviewDate);
            await service.GetFinancialPlanAsync();
            var history = await store.GetFinancialHistoryAsync();
            var snapshot = Assert.Single(history.Snapshots);
            var frozen = Assert.Single(history.Plans);

            Assert.Equal(FirstReviewDate, snapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, snapshot.NextReviewDate);
            Assert.Equal(FirstReviewDate, frozen.PeriodStart);
            Assert.Equal(SecondReviewDate, frozen.PeriodEnd);
            Assert.Equal(30_000m, frozen.PlannedLivingBudget);
            Assert.Empty(history.Actuals);
            Assert.Empty(await service.GetHistoryPeriodsAsync());
        });
    }

    [Fact]
    public async Task ReviewCadence_PreservesInitialPartialThenContinuesMonthly()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            Assert.False((await TestFactory.Service(
                store,
                FirstReviewDate.AddDays(-1))
                .GetPeriodReviewAvailabilityAsync()).IsDue);

            var september = TestFactory.Service(store, FirstReviewDate);
            var septemberAvailability = await september
                .GetPeriodReviewAvailabilityAsync();
            Assert.True(septemberAvailability.IsDue);
            var septemberContext = await september
                .GetPeriodReviewContextAsync();
            var firstResult = await september.FinalizePeriodReviewAsync(
                DefaultDraft(
                    septemberContext,
                    septemberContext.OriginalPlan.PlannedLivingBudget));

            Assert.Equal(FirstReviewDate, firstResult.NewSnapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, firstResult.NewSnapshot.NextReviewDate);
            var firstHistory = Assert.Single(
                await september.GetHistoryPeriodsAsync());
            Assert.Equal(InitialDate, firstHistory.OriginalPlan.PeriodStart);
            Assert.Equal(FirstReviewDate, firstHistory.OriginalPlan.PeriodEnd);

            var october = TestFactory.Service(store, SecondReviewDate);
            var octoberAvailability = await october
                .GetPeriodReviewAvailabilityAsync();
            Assert.True(octoberAvailability.IsDue);
            Assert.Equal(FirstReviewDate,
                octoberAvailability.PendingPlan!.PeriodStart);
            Assert.Equal(SecondReviewDate,
                octoberAvailability.PendingPlan.PeriodEnd);
            Assert.Equal(30_000m,
                octoberAvailability.PendingPlan.PlannedLivingBudget);
        });
    }

    [Fact]
    public async Task OverdueReview_UsesScheduledCheckpointAsNewSnapshotDate()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var late = TestFactory.Service(
                store,
                FirstReviewDate.AddDays(1));
            var context = await late.GetPeriodReviewContextAsync();
            var result = await late.FinalizePeriodReviewAsync(
                DefaultDraft(context, context.OriginalPlan.PlannedLivingBudget));

            Assert.Equal(FirstReviewDate, result.NewSnapshot.SnapshotDate);
            Assert.Equal(SecondReviewDate, result.NewSnapshot.NextReviewDate);
            Assert.Equal(FirstReviewDate.AddDays(1),
                DateOnly.FromDateTime(result.Actual.FinalizedAtUtc.Date));
        });
    }

    [Fact]
    public async Task JumpingPastMultipleCheckpoints_DoesNotInventActualHistory()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();

            var jumped = TestFactory.Service(
                store,
                SecondReviewDate.AddDays(1));
            var availability = await jumped
                .GetPeriodReviewAvailabilityAsync();
            var history = await store.GetFinancialHistoryAsync();

            Assert.True(availability.IsDue);
            Assert.Equal(InitialDate, availability.PendingPlan!.PeriodStart);
            Assert.Equal(FirstReviewDate, availability.PendingPlan.PeriodEnd);
            Assert.Empty(history.Actuals);
            Assert.Empty(await jumped.GetHistoryPeriodsAsync());
            Assert.Equal(InitialDate,
                Assert.Single(history.Snapshots, x => x.IsCurrent)
                    .SnapshotDate);
        });
    }

    [Fact]
    public async Task PendingLegacyPlan_IsRepairedWithoutDeletingUserData()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store, InitialDate);
            await service.LoadCanonicalDevelopmentDataAsync();
            await service.GetFinancialPlanAsync();
            var original = await store.GetFinancialHistoryAsync();
            var snapshot = Assert.Single(original.Snapshots);
            var plan = Assert.Single(original.Plans);
            var legacySnapshot = snapshot with
            {
                NextReviewDate = SecondReviewDate
            };
            var legacyPlan = plan with
            {
                Id = Guid.NewGuid(),
                PeriodStart = FirstReviewDate,
                PeriodEnd = SecondReviewDate,
                ReviewAvailableFrom = SecondReviewDate,
                PaymentLines = plan.PaymentLines.Select(x => x with
                {
                    Id = Guid.NewGuid()
                }).ToArray()
            };
            await store.ReplacePendingFinancialSnapshotPlanAsync(
                legacySnapshot,
                legacyPlan);

            await service.GetFinancialPlanAsync();
            var repaired = await store.GetFinancialHistoryAsync();
            var repairedSnapshot = Assert.Single(repaired.Snapshots);
            var repairedPlan = Assert.Single(repaired.Plans);

            Assert.Equal(FirstReviewDate,
                repairedSnapshot.NextReviewDate);
            Assert.Equal(InitialDate, repairedPlan.PeriodStart);
            Assert.Equal(FirstReviewDate, repairedPlan.PeriodEnd);
            Assert.Equal(20_322.58m,
                repairedPlan.PlannedLivingBudget);
            Assert.Empty(repaired.Actuals);
        });
    }

    [Fact]
    public async Task ActualActivityDates_MustStayInsideSnapshotReviewWindow()
    {
        await WithStore(async store =>
        {
            var initial = TestFactory.Service(store, InitialDate);
            await initial.LoadCanonicalDevelopmentDataAsync();
            await initial.GetFinancialPlanAsync();
            var review = TestFactory.Service(store, FirstReviewDate);
            var context = await review.GetPeriodReviewContextAsync();
            var valid = DefaultDraft(
                context,
                context.OriginalPlan.PlannedLivingBudget);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.FinalizePeriodReviewAsync(valid with
                {
                    Flows =
                    [
                        new ActualFlowDraft(
                            ActualFlowType.UnplannedIncome,
                            "Snapshot günündeki gelir",
                            "Diğer",
                            InitialDate,
                            1_000m)
                    ]
                }));

            var firstPayment = valid.Payments[0];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                review.FinalizePeriodReviewAsync(valid with
                {
                    Payments = valid.Payments.Select(x =>
                        x.PeriodPlanPaymentLineId ==
                        firstPayment.PeriodPlanPaymentLineId
                            ? x with
                            {
                                ActualPaymentDate =
                                    FirstReviewDate.AddDays(1)
                            }
                            : x).ToArray()
                }));
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

                var review = TestFactory.Service(first, FirstReviewDate);
                var context = await review.GetPeriodReviewContextAsync();
                var draft = DefaultDraft(context, 37_500m);
                var preview = await review.PreviewPeriodReviewAsync(draft);
                confirmed = preview.SuggestedStartingSavings;
                var result = await review.FinalizePeriodReviewAsync(
                    draft with { ConfirmedStartingSavings = confirmed });

                Assert.Equal(
                    FirstReviewDate,
                    result.NewSnapshot.SnapshotDate);
                Assert.Equal(
                    SecondReviewDate,
                    result.NewSnapshot.NextReviewDate);
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
                    FirstReviewDate);
                var plan = await restarted.GetFinancialPlanAsync();
                var history = await restartedStore
                    .GetFinancialHistoryAsync();
                var latest = Assert.Single(history.Snapshots, x =>
                    x.IsCurrent);

                Assert.Equal(FirstReviewDate, latest.SnapshotDate);
                Assert.Equal(confirmed,
                    plan.Settings.ProjectionStartingSavings);
                Assert.Equal(FirstReviewDate,
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
            var review = TestFactory.Service(store, FirstReviewDate);
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
            Assert.Equal(20_322.58m,
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
            var review = TestFactory.Service(store, FirstReviewDate);
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
            var review = TestFactory.Service(store, FirstReviewDate);
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
            Assert.Equal(before.RemainingInstallmentCount,
                after.RemainingInstallmentCount);
            Assert.True(after.IsActive);
            Assert.Equal(FirstReviewDate, after.NextPaymentDate);
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
            var review = TestFactory.Service(store, FirstReviewDate);
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
                var review = TestFactory.Service(storeA, FirstReviewDate);
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
                var fresh = TestFactory.Service(storeB, FirstReviewDate);
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

    private static FinancialSnapshot Snapshot(DateOnly date) => new()
    {
        SnapshotDate = date,
        ProjectionAnchorDate = date,
        ProjectionStartingSavings = 0m,
        SalaryDay = 10,
        IsCurrent = true,
        Source = FinancialSnapshotSource.Initial,
        CreatedAtUtc = new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            12,
            0,
            0,
            TimeSpan.Zero)
    };

    private static PeriodPlanSnapshot Freeze(
        FinancialPlan source,
        FinancialSnapshot snapshot)
    {
        var plan = source with
        {
            Settings = source.Settings with
            {
                ProjectionAnchorDate = snapshot.SnapshotDate,
                ProjectionStartingSavings =
                    snapshot.ProjectionStartingSavings,
                SalaryDay = snapshot.SalaryDay
            }
        };
        return new PeriodPlanSnapshotService(
            TestFactory.ProjectionCalculator(),
            new SalaryPeriodCalculator(),
            new SalaryResolver()).Freeze(
            plan,
            snapshot,
            snapshot.CreatedAtUtc);
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
