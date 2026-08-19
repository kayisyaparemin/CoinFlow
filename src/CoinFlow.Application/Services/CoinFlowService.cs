using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class CoinFlowService(
    ICoinFlowStore store,
    IClock clock,
    SalaryPeriodCalculator salaryPeriodCalculator,
    FinancialProjectionService projectionService,
    PurchaseSimulationCalculator simulationCalculator,
    InstallmentScheduleCalculator installmentScheduleCalculator,
    EmergencyFundCalculator emergencyFundCalculator)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public async Task ResetAllDataAsync(CancellationToken cancellationToken = default)
    {
        await store.ResetAllDataAsync(cancellationToken);
        await store.SaveSettingsAsync(new UserSettings
        {
            SalaryDay = 10,
            GamificationEnabled = true,
            DevelopmentSeedEnabled = false,
            TrackingStartedDate = clock.Today
        }, cancellationToken);
    }

    public async Task<FinanceData> GetFinanceDataAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var settingsTask = store.GetSettingsAsync(cancellationToken);
        var salaryTask = store.GetSalaryScheduleAsync(cancellationToken);
        var loansTask = store.GetLoansAsync(cancellationToken);
        var plansTask = store.GetPaymentPlansAsync(cancellationToken);
        var cardsTask = store.GetCreditCardsAsync(cancellationToken);
        var expensesTask = store.GetExpensesAsync(cancellationToken: cancellationToken);
        var snapshotsTask = store.GetSpendableBalanceSnapshotsAsync(cancellationToken);
        var emergencyTask = store.GetEmergencyFundAsync(cancellationToken);
        var transfersTask = store.GetEmergencyFundTransfersAsync(cancellationToken);

        await Task.WhenAll(
            settingsTask,
            salaryTask,
            loansTask,
            plansTask,
            cardsTask,
            expensesTask,
            snapshotsTask,
            emergencyTask,
            transfersTask);

        var settings = await settingsTask;
        if (settings.TrackingStartedDate is null)
        {
            settings = settings with { TrackingStartedDate = clock.Today };
            await store.SaveSettingsAsync(settings, cancellationToken);
        }

        return new FinanceData(
            settings,
            await salaryTask,
            await loansTask,
            await plansTask,
            await cardsTask,
            await expensesTask,
            await snapshotsTask,
            await emergencyTask,
            await transfersTask);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var data = await GetFinanceDataAsync(cancellationToken);
        return projectionService.BuildDashboard(data, date);
    }

    public async Task<IReadOnlyList<FutureMonthProjection>> GetFutureMonthsAsync(
        DateOnly? asOf = null,
        int monthCount = 12,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var data = await GetFinanceDataAsync(cancellationToken);
        return projectionService.BuildFuturePeriods(data, date, monthCount);
    }

    public async Task<PurchaseSimulationResult> SimulatePurchaseAsync(
        PurchaseSimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var data = await GetFinanceDataAsync(cancellationToken);
        var baseline = projectionService.BuildFuturePeriods(data, date, 12);
        return simulationCalculator.Calculate(request, baseline, data.CreditCards);
    }

    public async Task AddExpenseAsync(ExpenseDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "Harcama tutarı sıfırdan büyük olmalıdır.");
        }

        var expense = new Expense
        {
            Amount = draft.Amount,
            Date = draft.Date,
            Category = draft.Category,
            PaymentType = draft.PaymentType,
            Note = draft.Note.Trim(),
            CreditCardId = draft.CreditCardId,
            InstallmentCount = draft.InstallmentCount,
            FirstInstallmentDate = draft.FirstInstallmentDate,
            CreatedAtUtc = clock.UtcNow
        };

        CreditCard? targetCard = null;
        if (draft.PaymentType == ExpensePaymentType.CreditCard)
        {
            if (draft.CreditCardId is null)
            {
                throw new InvalidOperationException("Kredi kartı harcaması için kart seçilmelidir.");
            }

            targetCard = (await store.GetCreditCardsAsync(cancellationToken))
                .SingleOrDefault(x => x.Id == draft.CreditCardId.Value)
                ?? throw new InvalidOperationException("Seçilen kredi kartı bulunamadı.");
        }

        if (draft.PaymentType == ExpensePaymentType.NewInstallment &&
            (draft.InstallmentCount.GetValueOrDefault() < 1 || draft.FirstInstallmentDate is null))
        {
            throw new InvalidOperationException("Yeni taksit için taksit sayısı ve ilk ödeme tarihi gereklidir.");
        }

        await store.UpsertExpenseAsync(expense, cancellationToken);

        if (targetCard is not null)
        {
            var charge = new CardCharge
            {
                Id = expense.Id,
                CreditCardId = targetCard.Id,
                Description = string.IsNullOrWhiteSpace(expense.Note) ? "Kart harcaması" : expense.Note,
                PostingDate = expense.Date,
                Amount = expense.Amount
            };
            var updated = targetCard with
            {
                Charges = targetCard.Charges.Concat([charge]).ToArray()
            };
            updated = updated with { CurrentTotalDebt = CreditCardProjectionCalculator.DeriveCurrentTotalDebt(updated) };
            await store.UpsertCreditCardAsync(updated, cancellationToken);
        }
        else if (draft.PaymentType == ExpensePaymentType.NewInstallment)
        {
            await CreatePlanFromExpenseAsync(expense, cancellationToken);
        }
    }

    public Task SaveSalaryAsync(SalaryScheduleEntry entry, CancellationToken cancellationToken = default) =>
        store.UpsertSalaryAsync(entry, cancellationToken);

    public Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.DeleteSalaryAsync(id, cancellationToken);

    public Task SaveLoanAsync(Loan loan, CancellationToken cancellationToken = default) =>
        store.UpsertLoanAsync(loan, cancellationToken);

    public Task DeleteLoanAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.DeleteLoanAsync(id, cancellationToken);

    public Task SavePaymentPlanAsync(TemporaryPaymentPlan plan, CancellationToken cancellationToken = default) =>
        store.UpsertPaymentPlanAsync(plan, cancellationToken);

    public Task DeletePaymentPlanAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.DeletePaymentPlanAsync(id, cancellationToken);

    public Task SaveCreditCardAsync(CreditCard card, CancellationToken cancellationToken = default)
    {
        ValidateCreditCardPaymentSettings(card);
        var withAnchor = card with
        {
            BalanceAsOfDate = card.BalanceAsOfDate == default ? clock.Today : card.BalanceAsOfDate
        };
        var normalized = withAnchor with
        {
            CurrentTotalDebt = CreditCardProjectionCalculator.DeriveCurrentTotalDebt(withAnchor)
        };
        return store.UpsertCreditCardAsync(normalized, cancellationToken);
    }

    public Task DeleteCreditCardAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.DeleteCreditCardAsync(id, cancellationToken);

    public async Task SaveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CreditCardPaymentType paymentType,
        decimal? amount = null,
        CancellationToken cancellationToken = default)
    {
        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");
        if (paymentType == CreditCardPaymentType.FixedAmount && amount is null or <= 0m)
        {
            throw new InvalidOperationException("Özel ödeme tutarı sıfırdan büyük olmalıdır.");
        }

        var existing = card.PaymentPlans.FirstOrDefault(x => x.DueDate == dueDate);
        var paymentPlan = new CreditCardPaymentPlan
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            CreditCardId = creditCardId,
            DueDate = dueDate,
            PaymentType = paymentType,
            Amount = paymentType == CreditCardPaymentType.FixedAmount ? amount : null
        };
        var updated = card with
        {
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != dueDate)
                .Append(paymentPlan)
                .OrderBy(x => x.DueDate)
                .ToArray()
        };
        await SaveCreditCardAsync(updated, cancellationToken);
    }

    public async Task RemoveCreditCardPaymentPlanAsync(
        Guid creditCardId,
        DateOnly dueDate,
        CancellationToken cancellationToken = default)
    {
        var card = (await store.GetCreditCardsAsync(cancellationToken))
            .SingleOrDefault(x => x.Id == creditCardId)
            ?? throw new InvalidOperationException("Kredi kartı bulunamadı.");
        await SaveCreditCardAsync(card with
        {
            PaymentPlans = card.PaymentPlans.Where(x => x.DueDate != dueDate).ToArray()
        }, cancellationToken);
    }

    public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
        store.SaveSettingsAsync(
            settings.TrackingStartedDate is null
                ? settings with { TrackingStartedDate = clock.Today }
                : settings,
            cancellationToken);

    public Task SaveEmergencyFundAsync(EmergencyFund fund, CancellationToken cancellationToken = default) =>
        store.SaveEmergencyFundAsync(fund, cancellationToken);

    public async Task SaveSpendableBalanceSnapshotAsync(
        decimal amount,
        DateOnly snapshotDate,
        string note,
        CancellationToken cancellationToken = default)
    {
        if (snapshotDate > clock.Today)
        {
            throw new InvalidOperationException("Serbest bakiye tarihi gelecekte olamaz.");
        }

        var settings = await store.GetSettingsAsync(cancellationToken);
        var activePeriod = salaryPeriodCalculator.GetPeriod(clock.Today, settings.SalaryDay);
        if (!activePeriod.Contains(snapshotDate))
        {
            throw new InvalidOperationException("Serbest bakiye düzeltmesi yalnızca aktif maaş dönemi için yapılabilir.");
        }

        await store.UpsertSpendableBalanceSnapshotAsync(new SpendableBalanceSnapshot
        {
            Amount = amount,
            SnapshotDate = snapshotDate,
            SalaryPeriodStart = activePeriod.Start,
            CreatedAtUtc = clock.UtcNow,
            Note = note.Trim()
        }, cancellationToken);
    }

    public async Task TransferToEmergencyFundAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        var data = await GetFinanceDataAsync(cancellationToken);
        var period = salaryPeriodCalculator.GetPeriod(clock.Today, data.Settings.SalaryDay);
        var allocation = emergencyFundCalculator.AllocateTransfer(
            data.EmergencyFund,
            period.Start,
            amount,
            data.EmergencyFundTransfers);
        var now = clock.UtcNow;

        await store.UpsertEmergencyFundTransferAsync(new EmergencyFundTransfer
        {
            TransferDate = clock.Today,
            SalaryPeriodStart = period.Start,
            Amount = amount,
            CoveredPlannedAmount = allocation.CoveredPlannedAmount,
            CreatedAtUtc = now
        }, cancellationToken);

        if (allocation.ExtraSpendableAmount > 0m)
        {
            await store.UpsertExpenseAsync(new Expense
            {
                Amount = allocation.ExtraSpendableAmount,
                Date = clock.Today,
                Category = ExpenseCategory.Other,
                PaymentType = ExpensePaymentType.Cash,
                Note = "Plan dışı acil durum tamponu aktarımı",
                CreatedAtUtc = now
            }, cancellationToken);
        }

        await store.SaveEmergencyFundAsync(data.EmergencyFund with
        {
            CurrentAmount = data.EmergencyFund.CurrentAmount + amount
        }, cancellationToken);
    }

    private async Task CreatePlanFromExpenseAsync(Expense expense, CancellationToken cancellationToken)
    {
        var count = expense.InstallmentCount.GetValueOrDefault();
        var first = expense.FirstInstallmentDate;
        if (count < 1 || first is null)
        {
            throw new InvalidOperationException("Yeni taksit için taksit sayısı ve ilk ödeme tarihi gereklidir.");
        }

        var planId = Guid.NewGuid();
        var installments = installmentScheduleCalculator
            .Split(expense.Amount, count, first.Value)
            .Select(x => new TemporaryPaymentInstallment
            {
                PlanId = planId,
                DueDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        await store.UpsertPaymentPlanAsync(new TemporaryPaymentPlan
        {
            Id = planId,
            Name = string.IsNullOrWhiteSpace(expense.Note) ? "Yeni taksit" : expense.Note,
            Kind = PaymentPlanKind.PlannedInstallment,
            Installments = installments
        }, cancellationToken);
    }

    private static void ValidateCreditCardPaymentSettings(CreditCard card)
    {
        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException("Sabit ödeme stratejisi için pozitif tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy == ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException("Sabit projeksiyon varsayımı için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount && x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException("Özel kart ödeme tutarı sıfırdan büyük olmalıdır.");
        }
    }
}
