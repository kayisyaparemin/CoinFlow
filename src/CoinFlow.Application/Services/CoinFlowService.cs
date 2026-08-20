using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class CoinFlowService(
    ICoinFlowStore store,
    IClock clock,
    FinancialProjectionService projectionService,
    SimulationCalculator simulationCalculator,
    TargetAmountCalculator targetAmountCalculator)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public Task ResetDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        store.ResetAllDataAsync(cancellationToken);

    public async Task<FinancialPlan> GetFinancialPlanAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var settingsTask = store.GetSettingsAsync(cancellationToken);
        var salariesTask = store.GetSalaryScheduleAsync(cancellationToken);
        var incomesTask = store.GetOtherIncomesAsync(cancellationToken);
        var loansTask = store.GetLoansAsync(cancellationToken);
        var plansTask = store.GetPaymentPlansAsync(cancellationToken);
        var cardsTask = store.GetCreditCardsAsync(cancellationToken);
        var largeExpensesTask =
            store.GetPlannedLargeExpensesAsync(cancellationToken);

        await Task.WhenAll(
            settingsTask,
            salariesTask,
            incomesTask,
            loansTask,
            plansTask,
            cardsTask,
            largeExpensesTask);

        return new FinancialPlan
        {
            Settings = await settingsTask,
            Salaries = await salariesTask,
            OtherIncomes = await incomesTask,
            Loans = await loansTask,
            PaymentPlans = await plansTask,
            CreditCards = await cardsTask,
            PlannedLargeExpenses = await largeExpensesTask
        };
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var plan = await GetFinancialPlanAsync(cancellationToken);
        return projectionService.BuildDashboard(plan, date);
    }

    public async Task<IReadOnlyList<SalaryPeriodProjection>>
        GetFuturePeriodsAsync(
            DateOnly? asOf = null,
            int periodCount = 12,
            CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var plan = await GetFinancialPlanAsync(cancellationToken);
        return projectionService.BuildFuturePeriods(plan, date, periodCount);
    }

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var plan = await GetFinancialPlanAsync(cancellationToken);
        return simulationCalculator.Calculate(plan, date, request);
    }

    public async Task<SalaryPeriodProjection?> FindTargetPeriodAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var periods = await GetFuturePeriodsAsync(
            asOf,
            12,
            cancellationToken);
        return targetAmountCalculator.FindFirstReached(periods, targetAmount);
    }

    public async Task ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "Plan, açık kullanıcı onayı olmadan uygulanamaz.");
        }

        var current = await GetFinancialPlanAsync(cancellationToken);
        var scenario = simulationCalculator.BuildScenarioPlan(current, request);

        switch (request.Type)
        {
            case SimulationScenarioType.CashPurchase:
                await store.UpsertPlannedLargeExpenseAsync(
                    scenario.PlannedLargeExpenses
                        .Single(x => current.PlannedLargeExpenses
                            .All(existing => existing.Id != x.Id)),
                    cancellationToken);
                break;
            case SimulationScenarioType.CreditCardSinglePayment:
            case SimulationScenarioType.CreditCardInstallmentPurchase:
                var changedCard = scenario.CreditCards.Single(x =>
                    current.CreditCards.Single(existing => existing.Id == x.Id)
                        .Charges.Count != x.Charges.Count);
                await store.UpsertCreditCardAsync(changedCard, cancellationToken);
                break;
            case SimulationScenarioType.FinancingLoan:
            case SimulationScenarioType.CashDebt:
            case SimulationScenarioType.FutureOneTimePayment:
            case SimulationScenarioType.RecurringPayment:
                await store.UpsertPaymentPlanAsync(
                    scenario.PaymentPlans.Single(x =>
                        current.PaymentPlans.All(existing => existing.Id != x.Id)),
                    cancellationToken);
                break;
            case SimulationScenarioType.FutureIncome:
                await store.UpsertOtherIncomeAsync(
                    scenario.OtherIncomes.Single(x =>
                        current.OtherIncomes.All(existing => existing.Id != x.Id)),
                    cancellationToken);
                break;
            case SimulationScenarioType.SalaryChange:
                foreach (var existing in current.Salaries
                             .Where(x => x.EffectiveDate == request.StartDate))
                {
                    await store.DeleteSalaryAsync(existing.Id, cancellationToken);
                }

                await store.UpsertSalaryAsync(
                    scenario.Salaries.Single(x =>
                        current.Salaries.All(existing => existing.Id != x.Id)),
                    cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Type));
        }
    }

    public Task SaveSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Maaş tutarı sıfırdan büyük olmalıdır.");
        }

        return store.UpsertSalaryAsync(entry, cancellationToken);
    }

    public Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeleteSalaryAsync(id, cancellationToken);

    public Task SaveOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default)
    {
        if (income.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Gelir tutarı sıfırdan büyük olmalıdır.");
        }

        return store.UpsertOtherIncomeAsync(income, cancellationToken);
    }

    public Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeleteOtherIncomeAsync(id, cancellationToken);

    public Task SaveLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default)
    {
        if (loan.MonthlyPayment <= 0m ||
            loan.RemainingInstallmentCount < 1)
        {
            throw new InvalidOperationException(
                "Kredi taksiti ve kalan taksit sayısı pozitif olmalıdır.");
        }

        CalendarRules.ValidateDay(loan.PaymentDay);
        return store.UpsertLoanAsync(loan, cancellationToken);
    }

    public Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeleteLoanAsync(id, cancellationToken);

    public Task SavePaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Installments.Count == 0 ||
            plan.Installments.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Ödeme planında en az bir pozitif ödeme olmalıdır.");
        }

        return store.UpsertPaymentPlanAsync(plan, cancellationToken);
    }

    public Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeletePaymentPlanAsync(id, cancellationToken);

    public Task SavePlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default)
    {
        if (expense.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Büyük planlı ödeme tutarı pozitif olmalıdır.");
        }

        return store.UpsertPlannedLargeExpenseAsync(
            expense,
            cancellationToken);
    }

    public Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        store.DeletePlannedLargeExpenseAsync(id, cancellationToken);

    public Task SaveCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default)
    {
        ValidateCreditCardPaymentSettings(card);
        var normalized = card with
        {
            BalanceAsOfDate = card.BalanceAsOfDate == default
                ? clock.Today
                : card.BalanceAsOfDate
        };
        return store.UpsertCreditCardAsync(normalized, cancellationToken);
    }

    public Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
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
        if (paymentType == CreditCardPaymentType.FixedAmount &&
            amount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Özel ödeme tutarı sıfırdan büyük olmalıdır.");
        }

        var existing = card.PaymentPlans
            .FirstOrDefault(x => x.DueDate == dueDate);
        var paymentPlan = new CreditCardPaymentPlan
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            CreditCardId = creditCardId,
            DueDate = dueDate,
            PaymentType = paymentType,
            Amount = paymentType == CreditCardPaymentType.FixedAmount
                ? amount
                : null
        };
        await SaveCreditCardAsync(card with
        {
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != dueDate)
                .Append(paymentPlan)
                .OrderBy(x => x.DueDate)
                .ToArray()
        }, cancellationToken);
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
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != dueDate)
                .ToArray()
        }, cancellationToken);
    }

    public Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        CalendarRules.ValidateDay(settings.SalaryDay);
        if (settings.MonthlyLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Tahmini yaşam bütçesi negatif olamaz.");
        }

        return store.SaveSettingsAsync(settings, cancellationToken);
    }

    private static void ValidateCreditCardPaymentSettings(CreditCard card)
    {
        CalendarRules.ValidateDay(card.StatementClosingDay);
        CalendarRules.ValidateDay(card.PaymentDueDay);
        if (card.PaymentStrategy == CreditCardPaymentStrategy.FixedAmount &&
            card.FixedPaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit ödeme stratejisi için pozitif tutar gereklidir.");
        }

        if (card.ProjectionFallbackStrategy ==
                ProjectionFallbackStrategy.FixedAmount &&
            card.ProjectionFallbackFixedAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Sabit projeksiyon varsayımı için pozitif tutar gereklidir.");
        }

        if (card.PaymentPlans.Any(x =>
                x.PaymentType == CreditCardPaymentType.FixedAmount &&
                x.Amount is null or <= 0m))
        {
            throw new InvalidOperationException(
                "Özel kart ödeme tutarı sıfırdan büyük olmalıdır.");
        }
    }
}
