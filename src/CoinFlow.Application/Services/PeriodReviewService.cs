using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class PeriodReviewService(
    ICoinFlowStore store,
    IClock clock,
    FinancialSnapshotService snapshotService,
    FinancialStateReconciliationService reconciliationService,
    FinancialInstrumentReconciliationService instrumentService,
    PlanActualComparisonCalculator comparisonCalculator)
{
    public async Task<PeriodReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history);
        if (current is null)
        {
            return new PeriodReviewAvailability(
                false,
                false,
                null,
                null,
                null,
                "Güncel finansal durumunu oluşturarak başlayabilirsin.");
        }

        var plan = history.Plans
            .Where(x => x.FinancialSnapshotId == current.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var finalized = plan is not null && history.Actuals.Any(x =>
            x.PeriodPlanSnapshotId == plan.Id);
        var due = plan is not null &&
                  !finalized &&
                  clock.Today >= plan.ReviewAvailableFrom;
        var message = due
            ? $"{plan!.PeriodStart:dd MMMM} dönemi güncellenmeye hazır."
            : $"Son güncelleme: {current.SnapshotDate:dd MMMM yyyy}";
        return new PeriodReviewAvailability(
            true,
            due,
            current,
            due ? plan : null,
            current.SnapshotDate,
            message);
    }

    public async Task<PeriodReviewContext> GetContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history)
            ?? throw new InvalidOperationException(
                "Önce güncel finansal durumunu kaydetmelisin.");
        var plan = planId is null
            ? history.Plans
                .Where(x => x.FinancialSnapshotId == current.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault()
            : history.Plans.SingleOrDefault(x => x.Id == planId.Value);
        if (plan is null)
        {
            throw new InvalidOperationException(
                "Güncellenecek dönem planı bulunamadı.");
        }

        var source = history.Snapshots.Single(x =>
            x.Id == plan.FinancialSnapshotId);
        var revision = history.Revisions
            .Where(x => x.PeriodPlanSnapshotId == plan.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var actual = history.Actuals.SingleOrDefault(x =>
            x.PeriodPlanSnapshotId == plan.Id);
        var comparison = actual is null
            ? null
            : comparisonCalculator.Calculate(plan, revision, actual);
        return new PeriodReviewContext(
            source,
            plan,
            revision,
            actual,
            source.ProjectionStartingSavings +
            plan.PlannedIncome -
            plan.PaymentLines.Sum(x =>
                x.PlannedAmount.GetValueOrDefault()) -
            plan.PlannedLivingBudget -
            plan.PlannedDeficitInterest,
            comparison);
    }

    public async Task<PeriodReviewPreview> PreviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var plan = history.Plans.SingleOrDefault(x =>
            x.Id == draft.PeriodPlanSnapshotId)
            ?? throw new InvalidOperationException("Dönem planı bulunamadı.");
        var snapshot = history.Snapshots.Single(x =>
            x.Id == plan.FinancialSnapshotId);
        var revision = BuildRevision(
            plan,
            draft.RevisedLivingBudget,
            draft.RevisionNote);
        var actual = BuildActual(
            plan,
            snapshot,
            revision,
            draft,
            Guid.Empty,
            false);
        var comparison = comparisonCalculator.Calculate(
            plan,
            revision,
            actual);
        return new PeriodReviewPreview(
            actual.DerivedEndingSavings,
            actual.ConfirmedEndingSavings,
            actual.ReconciliationAdjustment,
            comparison);
    }

    public async Task<FinancialReviewResult> FinalizeAsync(
        FinancialPlan financialPlan,
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var current = FinancialSnapshotService.LatestCurrent(history)
            ?? throw new InvalidOperationException(
                "Güncel finansal durum bulunamadı.");
        var plan = history.Plans.SingleOrDefault(x =>
            x.Id == draft.PeriodPlanSnapshotId)
            ?? throw new InvalidOperationException("Dönem planı bulunamadı.");
        if (plan.FinancialSnapshotId != current.Id)
        {
            throw new InvalidOperationException(
                "Yalnızca güncel plan dönemi kapatılabilir.");
        }

        if (history.Actuals.Any(x =>
                x.PeriodPlanSnapshotId == plan.Id))
        {
            throw new InvalidOperationException(
                "Bu dönem daha önce kaydedildi.");
        }

        if (clock.Today < plan.ReviewAvailableFrom)
        {
            throw new InvalidOperationException(
                "Bu dönem henüz güncellenmeye hazır değil.");
        }

        var revision = BuildRevision(
            plan,
            draft.RevisedLivingBudget,
            draft.RevisionNote);
        var provisional = BuildActual(
            plan,
            current,
            revision,
            draft,
            Guid.Empty,
            true);
        var instruments = instrumentService.Apply(
            financialPlan,
            plan,
            provisional.Payments,
            clock.Today);
        var updatedPlan = financialPlan with
        {
            Loans = instruments.Loans,
            PaymentPlans = instruments.PaymentPlans,
            CreditCards = instruments.CreditCards,
            PlannedLargeExpenses = instruments.LargeExpenses
        };
        var newBundle = snapshotService.Build(
            updatedPlan,
            provisional.ConfirmedEndingSavings,
            clock.Today,
            FinancialSnapshotSource.MonthlyUpdate,
            "Dönem güncellemesi",
            current.Id);
        var actual = provisional with
        {
            ResultFinancialSnapshotId = newBundle.Snapshot.Id
        };
        var comparison = comparisonCalculator.Calculate(
            plan,
            revision,
            actual);
        actual = actual with { ComparisonSummary = comparison.Summary };

        await store.FinalizeFinancialReviewAsync(
            new FinancialReviewCommit(
                revision,
                actual,
                newBundle.Snapshot,
                newBundle.Plan,
                newBundle.UpdatedSettings,
                instruments.Loans,
                instruments.PaymentPlans,
                instruments.CreditCards,
                instruments.LargeExpenses),
            cancellationToken);

        return new FinancialReviewResult(
            newBundle.Snapshot,
            actual,
            comparison,
            newBundle.Plan);
    }

    private PeriodActual BuildActual(
        PeriodPlanSnapshot plan,
        FinancialSnapshot snapshot,
        PeriodPlanRevision? revision,
        PeriodReviewDraft draft,
        Guid resultSnapshotId,
        bool validateReviewDate)
    {
        if (draft.ActualLivingSpend < 0m || draft.ActualInterest < 0m)
        {
            throw new InvalidOperationException(
                "Gerçekleşen tutarlar negatif olamaz.");
        }

        if (draft.LivingBreakdown.Any(x => x.Amount < 0m) ||
            draft.LivingBreakdown.Sum(x => x.Amount) >
            draft.ActualLivingSpend)
        {
            throw new InvalidOperationException(
                "Yaşam ayrıntıları toplam yaşam giderini aşamaz.");
        }

        if (draft.Flows.Any(x =>
                x.Amount <= 0m || string.IsNullOrWhiteSpace(x.Name)))
        {
            throw new InvalidOperationException(
                "Plan dışı gelir ve ödemelerde ad ile pozitif tutar gereklidir.");
        }

        if (validateReviewDate && draft.Flows.Any(x =>
                x.Date > clock.Today))
        {
            throw new InvalidOperationException(
                "Plan dışı gelir veya ödeme tarihi gelecekte olamaz.");
        }

        var actualId = Guid.NewGuid();
        var paymentDrafts = draft.Payments
            .GroupBy(x => x.PeriodPlanPaymentLineId)
            .ToDictionary(x => x.Key, x => x.Single());
        var payments = plan.PaymentLines.Select(line =>
        {
            var input = paymentDrafts.GetValueOrDefault(line.Id)
                ?? new ActualPaymentDraft(
                    line.Id,
                    line.PlannedAmount is null
                        ? ActualPaymentStatus.Unpaid
                        : ActualPaymentStatus.Paid,
                    line.PlannedAmount.GetValueOrDefault(),
                    line.PlannedAmount is null ? null : line.PlannedDate);
            var amount = input.Status == ActualPaymentStatus.Unpaid
                ? 0m
                : input.ActualAmount;
            if (amount < 0m)
            {
                throw new InvalidOperationException(
                    "Gerçek ödeme tutarı negatif olamaz.");
            }
            if (input.Status != ActualPaymentStatus.Unpaid && amount <= 0m)
            {
                throw new InvalidOperationException(
                    "Ödendi olarak işaretlenen tutar sıfırdan büyük olmalıdır.");
            }

            if (validateReviewDate && input.ActualPaymentDate > clock.Today)
            {
                throw new InvalidOperationException(
                    "Gerçek ödeme tarihi gelecekte olamaz.");
            }

            var normalizedStatus =
                input.Status == ActualPaymentStatus.Paid &&
                line.PlannedAmount is decimal plannedAmount &&
                plannedAmount != amount
                    ? ActualPaymentStatus.DifferentAmount
                    : input.Status;
            return new ActualPayment
            {
                PeriodActualId = actualId,
                PeriodPlanPaymentLineId = line.Id,
                SourceEntityId = line.SourceEntityId,
                SourceType = line.SourceType,
                Name = line.Name,
                PlannedDate = line.PlannedDate,
                PlannedAmount = line.PlannedAmount,
                ActualPaymentDate = input.Status ==
                                    ActualPaymentStatus.Unpaid
                    ? null
                    : input.ActualPaymentDate ?? line.PlannedDate,
                ActualAmount = amount,
                Status = normalizedStatus,
                Note = input.Note.Trim()
            };
        }).ToArray();
        var flows = draft.Flows.Select(x => new ActualFlow
        {
            PeriodActualId = actualId,
            Type = x.Type,
            Name = x.Name.Trim(),
            Category = x.Category.Trim(),
            Date = x.Date,
            Amount = x.Amount
        }).ToArray();
        var breakdown = draft.LivingBreakdown
            .Where(x => x.Amount > 0m)
            .Select(x => new ActualLivingBreakdown
            {
                PeriodActualId = actualId,
                Category = x.Category.Trim(),
                Amount = x.Amount
            }).ToArray();
        var baselineIncome = revision?.PlannedIncome ?? plan.PlannedIncome;
        var derived = reconciliationService.CalculateSuggestedSavings(
            snapshot,
            baselineIncome,
            payments,
            draft.ActualLivingSpend,
            draft.ActualInterest,
            flows);
        var confirmed = draft.ConfirmedStartingSavings ?? derived;
        decimal Sum(PlanPaymentSourceType type) => payments
            .Where(x => x.SourceType == type)
            .Sum(x => x.ActualAmount);
        var unplannedIncome = flows
            .Where(x => x.Type == ActualFlowType.UnplannedIncome)
            .Sum(x => x.Amount);
        var unplannedPayments = flows
            .Where(x => x.Type == ActualFlowType.UnplannedPayment)
            .Sum(x => x.Amount);
        var mandatory = payments
            .Where(x => x.SourceType !=
                        PlanPaymentSourceType.PlannedLargeExpense)
            .Sum(x => x.ActualAmount);

        return new PeriodActual
        {
            Id = actualId,
            PeriodPlanSnapshotId = plan.Id,
            SourceFinancialSnapshotId = snapshot.Id,
            ResultFinancialSnapshotId = resultSnapshotId,
            PeriodStart = plan.PeriodStart,
            PeriodEnd = plan.PeriodEnd,
            FinalizedAtUtc = clock.UtcNow,
            ActualIncome = baselineIncome + unplannedIncome,
            ActualLoanPayments = Sum(PlanPaymentSourceType.Loan),
            ActualCardPayments = Sum(PlanPaymentSourceType.CreditCard),
            ActualTemporaryPayments =
                Sum(PlanPaymentSourceType.TemporaryPayment),
            ActualInstallmentPayments =
                Sum(PlanPaymentSourceType.InstallmentPayment),
            ActualOtherScheduledPayments =
                Sum(PlanPaymentSourceType.OtherScheduledPayment),
            ActualLargeExpenses =
                Sum(PlanPaymentSourceType.PlannedLargeExpense),
            ActualMandatoryPayments = mandatory,
            ActualLivingSpend = draft.ActualLivingSpend,
            ActualInterest = draft.ActualInterest,
            UnplannedIncome = unplannedIncome,
            UnplannedPayments = unplannedPayments,
            DerivedEndingSavings = derived,
            ConfirmedEndingSavings = confirmed,
            ReconciliationAdjustment = confirmed - derived,
            Note = draft.ActualNote.Trim(),
            Payments = payments,
            Flows = flows,
            LivingBreakdown = breakdown
        };
    }

    private PeriodPlanRevision? BuildRevision(
        PeriodPlanSnapshot plan,
        decimal? revisedLivingBudget,
        string note)
    {
        if (revisedLivingBudget is null ||
            revisedLivingBudget.Value == plan.PlannedLivingBudget)
        {
            return null;
        }

        if (revisedLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Revize yaşam planı negatif olamaz.");
        }

        var livingDifference = revisedLivingBudget.Value -
                               plan.PlannedLivingBudget;
        return new PeriodPlanRevision
        {
            PeriodPlanSnapshotId = plan.Id,
            CreatedAtUtc = clock.UtcNow,
            PlannedIncome = plan.PlannedIncome,
            PlannedMandatoryPayments = plan.PlannedMandatoryPayments,
            PlannedLivingBudget = revisedLivingBudget.Value,
            PlannedLargeExpenses = plan.PlannedLargeExpenses,
            PlannedInterest = plan.PlannedCardInterest +
                              plan.PlannedDeficitInterest,
            PlannedEndingSavings = plan.PlannedEndingSavings -
                                   livingDifference,
            Note = note.Trim()
        };
    }
}
