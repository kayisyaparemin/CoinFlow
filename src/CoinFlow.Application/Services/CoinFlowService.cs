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
    TargetAmountCalculator targetAmountCalculator,
    PaymentAssignmentStrategyResolver strategyResolver,
    SalaryPeriodCalculator salaryPeriodCalculator,
    ProjectionBoundaryResolver projectionBoundaryResolver,
    FinancialSnapshotService snapshotService,
    HistoricalPlanRevisionService historicalPlanRevisionService,
    PeriodReviewService reviewService,
    HistoryQueryService historyService)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public Task ClearDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        store.ClearAllFinancialDataAsync(cancellationToken);

    public Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default) =>
        store.LoadCanonicalDevelopmentDataAsync(cancellationToken);

    public async Task<FinancialPlan> GetFinancialPlanAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadFinancialPlanCoreAsync(cancellationToken);
        await snapshotService.EnsureInitialSnapshotAsync(
            plan,
            cancellationToken);
        await historicalPlanRevisionService.CaptureOpenPlanRevisionAsync(
            plan,
            "Açık plan otomatik güncellendi",
            cancellationToken);
        return plan;
    }

    private async Task<FinancialPlan> LoadFinancialPlanCoreAsync(
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
        var strategiesTask =
            store.GetPaymentAssignmentStrategiesAsync(cancellationToken);

        await Task.WhenAll(
            settingsTask,
            salariesTask,
            incomesTask,
            loansTask,
            plansTask,
            cardsTask,
            largeExpensesTask,
            strategiesTask);

        var plan = new FinancialPlan
        {
            Settings = await settingsTask,
            Salaries = await salariesTask,
            OtherIncomes = await incomesTask,
            Loans = await loansTask,
            PaymentPlans = await plansTask,
            CreditCards = await cardsTask,
            PlannedLargeExpenses = await largeExpensesTask,
            PaymentAssignmentStrategies = await strategiesTask
        };
        return plan;
    }

    private async Task CapturePlanningChangeAsync(
        string trigger,
        CancellationToken cancellationToken)
    {
        var plan = await LoadFinancialPlanCoreAsync(cancellationToken);
        await snapshotService.EnsureInitialSnapshotAsync(
            plan,
            cancellationToken);
        await historicalPlanRevisionService.CaptureOpenPlanRevisionAsync(
            plan,
            trigger,
            cancellationToken);
    }

    public async Task<DashboardSnapshot?> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            return null;
        }

        return projectionService.BuildDashboard(
            query.Plan,
            date,
            query.Boundary?.FirstUnrealizedSalaryDate);
    }

    public async Task<IReadOnlyList<SalaryPeriodProjection>>
        GetFuturePeriodsAsync(
            DateOnly? asOf = null,
            int periodCount = 12,
            CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            return [];
        }

        return projectionService.BuildFuturePeriods(
            query.Plan,
            date,
            periodCount,
            query.Boundary?.FirstUnrealizedSalaryDate);
    }

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default) =>
        await SimulateAsync(
            [request],
            asOf,
            cancellationToken);

    public async Task<SimulationResult> SimulateAsync(
        IReadOnlyList<SimulationRequest> requests,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var query = await GetProjectionPlanAsync(date, cancellationToken);
        if (!CanBuildProjection(query.Plan))
        {
            throw new InvalidOperationException(
                "Simülasyon yapabilmek için önce maaşını ve maaş kullanım düzenini oluştur.");
        }

        return simulationCalculator.Calculate(
            query.Plan,
            date,
            requests,
            firstSalaryDate: query.Boundary?.FirstUnrealizedSalaryDate);
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

    public async Task<TargetReachabilityResult> FindTargetReachabilityAsync(
        decimal targetAmount,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var periods = await GetFuturePeriodsAsync(
            asOf,
            12,
            cancellationToken);
        return FindTargetReachability(periods, targetAmount);
    }

    public TargetReachabilityResult FindTargetReachability(
        IReadOnlyList<SalaryPeriodProjection> periods,
        decimal targetAmount) =>
        targetAmountCalculator.FindFirstReachable(periods, targetAmount);

    public async Task<SimulationApplyResult> ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default) =>
        await ApplySimulationAsync(
            [request],
            confirmed,
            cancellationToken);

    public async Task<SimulationApplyResult> ApplySimulationAsync(
        IReadOnlyList<SimulationRequest> requests,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "Plan, açık kullanıcı onayı olmadan uygulanamaz.");
        }

        SimulationCalculator.Validate(requests);
        if (requests.Any(x => x.ScenarioId == Guid.Empty))
        {
            throw new InvalidOperationException(
                "Uygulanacak simülasyon kimliği bulunamadı. Planı yeniden simüle edin.");
        }

        var current = await GetFinancialPlanAsync(cancellationToken);
        var existingResults = requests
            .Select(request => FindAppliedSimulation(current, request))
            .ToArray();
        if (existingResults.All(x => x is not null))
        {
            var first = existingResults[0]!;
            return first with
            {
                AlreadyApplied = true,
                Message = requests.Count == 1
                    ? first.Message
                    : "Bu simülasyon planı daha önce finans planına eklendi."
            };
        }

        if (existingResults.Any(x => x is not null))
        {
            throw new InvalidOperationException(
                "Bu simülasyon planının bir kısmı daha önce uygulanmış. Tekrar kaydı önlemek için planı temizleyip yeniden oluştur.");
        }

        ValidateSimulationApplyConflicts(current, requests);

        var scenario = simulationCalculator.BuildScenarioPlan(current, requests);
        var batch = BuildSimulationPersistenceBatch(scenario, requests);
        await store.ApplySimulationBatchAsync(batch, cancellationToken);

        await CapturePlanningChangeAsync(
            "Simülasyon planı uygulandı",
            cancellationToken);
        return AppliedResult(requests, batch);
    }

    private static void ValidateSimulationApplyConflicts(
        FinancialPlan current,
        IReadOnlyList<SimulationRequest> requests)
    {
        var conflictingSalary = requests.FirstOrDefault(request =>
            request.Type == SimulationScenarioType.SalaryChange &&
            current.Salaries.Any(x => x.EffectiveDate == request.StartDate));
        if (conflictingSalary is not null)
        {
            throw new InvalidOperationException(
                "Bu tarihte zaten bir maaş kaydı var. Geçmişi korumak için farklı bir geçerlilik tarihi seçin.");
        }

        var conflictingStrategy = requests.FirstOrDefault(request =>
            request.Type == SimulationScenarioType.PaymentStrategyChange &&
            current.PaymentAssignmentStrategies.Any(x =>
                x.EffectiveFromSalaryDate ==
                (request.EffectiveSalaryDate ?? request.StartDate)));
        if (conflictingStrategy is not null)
        {
            throw new InvalidOperationException(
                "Bu maaş tarihinde zaten bir kullanım düzeni var. Önceki kayıt değiştirilemez.");
        }
    }

    private static SimulationPersistenceBatch BuildSimulationPersistenceBatch(
        FinancialPlan scenario,
        IReadOnlyList<SimulationRequest> requests)
    {
        var requestIds = requests.Select(x => x.ScenarioId).ToHashSet();
        var cardIds = requests
            .Where(x => x.Type is
                SimulationScenarioType.CreditCardSinglePayment or
                SimulationScenarioType.CreditCardInstallmentPurchase or
                SimulationScenarioType.CreditCardFullPayment)
            .Select(x => x.CreditCardId ?? throw new InvalidOperationException(
                "Kart koşulunda kredi kartı bulunamadı."))
            .Distinct()
            .ToHashSet();

        return new SimulationPersistenceBatch(
            scenario.PlannedLargeExpenses
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.PaymentPlans
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.CreditCards
                .Where(x => cardIds.Contains(x.Id))
                .ToArray(),
            scenario.OtherIncomes
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.Salaries
                .Where(x => requestIds.Contains(x.Id))
                .ToArray(),
            scenario.PaymentAssignmentStrategies
                .Where(x => requestIds.Contains(x.Id))
                .ToArray());
    }

    private static SimulationApplyResult AppliedResult(
        IReadOnlyList<SimulationRequest> requests,
        SimulationPersistenceBatch batch)
    {
        if (requests.Count == 1)
        {
            var request = requests[0];
            return request.Type switch
            {
                SimulationScenarioType.CashPurchase =>
                    AppliedResult(
                        request,
                        batch.PlannedLargeExpenses.Single().Id,
                        SimulationApplyDestination.Payments,
                        "Plan finans planına eklendi."),
                SimulationScenarioType.CreditCardSinglePayment or
                    SimulationScenarioType.CreditCardInstallmentPurchase or
                    SimulationScenarioType.CreditCardFullPayment =>
                    AppliedResult(
                        request,
                        batch.CreditCards.Single().Id,
                        SimulationApplyDestination.CreditCard,
                        $"Plan {batch.CreditCards.Single().Bank} {batch.CreditCards.Single().Name} kartına eklendi."),
                SimulationScenarioType.FinancingLoan or
                    SimulationScenarioType.CashDebt or
                    SimulationScenarioType.FutureOneTimePayment or
                    SimulationScenarioType.RecurringPayment =>
                    AppliedResult(
                        request,
                        batch.PaymentPlans.Single().Id,
                        SimulationApplyDestination.Payments,
                        "Plan finans planına eklendi."),
                SimulationScenarioType.FutureIncome =>
                    AppliedResult(
                        request,
                        batch.OtherIncomes.Single().Id,
                        SimulationApplyDestination.Income,
                        "Gelir finans planına eklendi."),
                SimulationScenarioType.SalaryChange =>
                    AppliedResult(
                        request,
                        batch.Salaries.Single().Id,
                        SimulationApplyDestination.SalaryHistory,
                        "Maaş değişikliği kaydedildi."),
                SimulationScenarioType.PaymentStrategyChange =>
                    AppliedResult(
                        request,
                        batch.PaymentAssignmentStrategies.Single().Id,
                        SimulationApplyDestination.Settings,
                        "Maaş kullanım düzeni kaydedildi."),
                _ => throw new ArgumentOutOfRangeException(nameof(requests))
            };
        }

        return new SimulationApplyResult(
            requests[0].ScenarioId,
            Guid.Empty,
            SimulationApplyDestination.Payments,
            AlreadyApplied: false,
            $"{requests.Count} koşul finans planına eklendi.");
    }

    private static SimulationApplyResult? FindAppliedSimulation(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var entityId = request.ScenarioId;
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase
                when plan.PlannedLargeExpenses.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Payments,
                    "Plan daha önce finans planına eklendi."),
            SimulationScenarioType.CreditCardSinglePayment or
                SimulationScenarioType.CreditCardInstallmentPurchase
                when plan.CreditCards.Any(card =>
                    card.Id == request.CreditCardId &&
                    card.Charges.Any(charge => charge.Id == entityId)) =>
                AppliedResult(request, request.CreditCardId!.Value,
                    SimulationApplyDestination.CreditCard,
                    "Plan daha önce kredi kartına eklendi."),
            SimulationScenarioType.CreditCardFullPayment
                when plan.CreditCards.Any(card =>
                    card.Id == request.CreditCardId &&
                    card.PaymentPlans.Any(payment =>
                        payment.Id == entityId)) =>
                AppliedResult(request, request.CreditCardId!.Value,
                    SimulationApplyDestination.CreditCard,
                    "Tam ödeme planı daha önce kredi kartına eklendi."),
            SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.FutureOneTimePayment or
                SimulationScenarioType.RecurringPayment
                when plan.PaymentPlans.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Payments,
                    "Plan daha önce finans planına eklendi."),
            SimulationScenarioType.FutureIncome
                when plan.OtherIncomes.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Income,
                    "Gelir daha önce finans planına eklendi."),
            SimulationScenarioType.SalaryChange
                when plan.Salaries.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.SalaryHistory,
                    "Maaş değişikliği daha önce kaydedildi."),
            SimulationScenarioType.PaymentStrategyChange
                when plan.PaymentAssignmentStrategies.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Settings,
                    "Maaş kullanım düzeni değişikliği daha önce kaydedildi."),
            _ => null
        };
    }

    private static SimulationApplyResult AppliedResult(
        SimulationRequest request,
        Guid entityId,
        SimulationApplyDestination destination,
        string message) => new(
            request.ScenarioId,
            entityId,
            destination,
            AlreadyApplied: false,
            message);

    public async Task<InitialPaymentStrategySetup?> SaveSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Maaş tutarı sıfırdan büyük olmalıdır.");
        }

        await store.UpsertSalaryAsync(entry, cancellationToken);
        await CapturePlanningChangeAsync(
            "Maaş planı değişti",
            cancellationToken);
        return await GetInitialPaymentStrategySetupAsync(cancellationToken);
    }

    public async Task<InitialPaymentStrategySetup?>
        GetInitialPaymentStrategySetupAsync(
            CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        if (plan.Salaries.Count == 0 ||
            plan.PaymentAssignmentStrategies.Count > 0)
        {
            return null;
        }

        var settings = plan.Settings;
        var anchor = settings.ProjectionAnchorDate;
        if (anchor == default)
        {
            anchor = clock.Today;
            settings = settings with { ProjectionAnchorDate = anchor };
            await store.SaveSettingsAsync(settings, cancellationToken);
        }

        var effectiveSalary = salaryPeriodCalculator
            .GetFirstSalaryOnOrAfter(anchor, settings.SalaryDay);
        var exampleSalary = CalendarRules.AddMonthsKeepingDay(
            effectiveSalary,
            1,
            settings.SalaryDay);
        return new InitialPaymentStrategySetup(
            anchor,
            effectiveSalary,
            exampleSalary,
            effectiveSalary,
            CalendarRules.AddMonthsKeepingDay(
                exampleSalary,
                1,
                settings.SalaryDay));
    }

    public async Task CompleteInitialPaymentStrategySetupAsync(
        PaymentAssignmentMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new InvalidOperationException(
                "Maaş kullanım düzeni geçersiz.");
        }

        var setup = await GetInitialPaymentStrategySetupAsync(
            cancellationToken) ?? throw new InvalidOperationException(
                "İlk maaş kullanım düzeni kurulumu gerekli değil veya zaten tamamlandı.");
        await store.UpsertPaymentAssignmentStrategyAsync(
            new PaymentAssignmentStrategy
            {
                Mode = mode,
                EffectiveFromSalaryDate = setup.EffectiveSalaryDate,
                CreatedAt = clock.UtcNow,
                Note = "İlk maaş kullanım düzeni"
            },
            cancellationToken);
        await GetFinancialPlanAsync(cancellationToken);
    }

    public async Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteSalaryAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Maaş planı değişti",
            cancellationToken);
    }

    public async Task SaveOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default)
    {
        if (income.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Gelir tutarı sıfırdan büyük olmalıdır.");
        }

        await store.UpsertOtherIncomeAsync(income, cancellationToken);
        await CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
    }

    public async Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteOtherIncomeAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Gelir planı değişti",
            cancellationToken);
    }

    public async Task SaveLoanAsync(
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
        await store.UpsertLoanAsync(loan, cancellationToken);
        await CapturePlanningChangeAsync(
            "Kredi planı değişti",
            cancellationToken);
    }

    public async Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteLoanAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Kredi planı değişti",
            cancellationToken);
    }

    public async Task SavePaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Installments.Count == 0 ||
            plan.Installments.Any(x => x.Amount <= 0m))
        {
            throw new InvalidOperationException(
                "Ödeme planında en az bir pozitif ödeme olmalıdır.");
        }

        await store.UpsertPaymentPlanAsync(plan, cancellationToken);
        await CapturePlanningChangeAsync(
            "Planlı ödeme değişti",
            cancellationToken);
    }

    public async Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeletePaymentPlanAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Planlı ödeme değişti",
            cancellationToken);
    }

    public async Task SavePlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default)
    {
        if (expense.Amount <= 0m)
        {
            throw new InvalidOperationException(
                "Planlı büyük ödeme tutarı 0'dan büyük olmalı.");
        }

        await store.UpsertPlannedLargeExpenseAsync(
            expense,
            cancellationToken);
        await CapturePlanningChangeAsync(
            "Büyük ödeme planı değişti",
            cancellationToken);
    }

    public async Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeletePlannedLargeExpenseAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Büyük ödeme planı değişti",
            cancellationToken);
    }

    public async Task SaveCreditCardAsync(
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
        await store.UpsertCreditCardAsync(normalized, cancellationToken);
        await CapturePlanningChangeAsync(
            "Kart planı değişti",
            cancellationToken);
    }

    public async Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await store.DeleteCreditCardAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Kart planı değişti",
            cancellationToken);
    }

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

    public async Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        CalendarRules.ValidateDay(settings.SalaryDay);
        if (settings.MonthlyLivingBudget < 0m)
        {
            throw new InvalidOperationException(
                "Tahmini yaşam bütçesi negatif olamaz.");
        }

        if (settings.CreditCardCarryInterestRate is < 0m or > 1m ||
            settings.DeficitFinancingInterestRate is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                "Faiz varsayımları %0 ile %100 arasında olmalıdır.");
        }

        var plan = await GetFinancialPlanAsync(cancellationToken);
        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        var adjustedStrategies = settings.SalaryDay == plan.Settings.SalaryDay
            ? plan.PaymentAssignmentStrategies
            : plan.PaymentAssignmentStrategies.Select(strategy =>
                strategy with
                {
                    EffectiveFromSalaryDate = CalendarRules.ResolveDay(
                        strategy.EffectiveFromSalaryDate.Year,
                        strategy.EffectiveFromSalaryDate.Month,
                        settings.SalaryDay)
                }).ToArray();
        var createsRecoverySnapshot = currentSnapshot is not null &&
                                      CanBuildProjection(plan) &&
                                      (settings.ProjectionStartingSavings !=
                                           plan.Settings.ProjectionStartingSavings ||
                                       settings.ProjectionAnchorDate !=
                                           plan.Settings.ProjectionAnchorDate);
        if (createsRecoverySnapshot)
        {
            var snapshotDate = settings.ProjectionStartingSavings !=
                               plan.Settings.ProjectionStartingSavings
                ? clock.Today
                : settings.ProjectionAnchorDate;
            var normalized = settings with
            {
                ProjectionAnchorDate = snapshotDate
            };
            await snapshotService.CreateCurrentSnapshotAsync(
                plan with
                {
                    Settings = normalized,
                    PaymentAssignmentStrategies = adjustedStrategies
                },
                normalized.ProjectionStartingSavings,
                snapshotDate,
                FinancialSnapshotSource.Recovery,
                "Güncel finansal durum yenilendi",
                cancellationToken);
            settings = normalized;
        }
        else
        {
            await store.SaveSettingsAsync(settings, cancellationToken);
        }
        if (settings.SalaryDay != plan.Settings.SalaryDay)
        {
            foreach (var strategy in adjustedStrategies)
            {
                await store.UpsertPaymentAssignmentStrategyAsync(
                    strategy,
                    cancellationToken);
            }
        }

        await CapturePlanningChangeAsync(
            "Planlama varsayımları değişti",
            cancellationToken);
    }

    public async Task<PeriodReviewAvailability>
        GetPeriodReviewAvailabilityAsync(
            CancellationToken cancellationToken = default)
    {
        await GetFinancialPlanAsync(cancellationToken);
        return await reviewService.GetAvailabilityAsync(cancellationToken);
    }

    public Task<PeriodReviewContext> GetPeriodReviewContextAsync(
        Guid? planId = null,
        CancellationToken cancellationToken = default) =>
        reviewService.GetContextAsync(planId, cancellationToken);

    public Task<PeriodReviewPreview> PreviewPeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default) =>
        reviewService.PreviewAsync(draft, cancellationToken);

    public async Task<FinancialReviewResult> FinalizePeriodReviewAsync(
        PeriodReviewDraft draft,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        return await reviewService.FinalizeAsync(
            plan,
            draft,
            cancellationToken);
    }

    public Task<IReadOnlyList<HistoryPeriod>> GetHistoryPeriodsAsync(
        CancellationToken cancellationToken = default) =>
        historyService.GetPeriodsAsync(cancellationToken);

    public Task<HistoryPeriod> GetHistoryPeriodAsync(
        Guid actualId,
        CancellationToken cancellationToken = default) =>
        historyService.GetPeriodAsync(actualId, cancellationToken);

    public Task<HistorySummary?> GetHistorySummaryAsync(
        int periodCount = 3,
        CancellationToken cancellationToken = default) =>
        historyService.GetRecentSummaryAsync(
            periodCount,
            cancellationToken);

    public async Task<FinancialSnapshot> RefreshCurrentFinancialStateAsync(
        decimal startingSavings,
        string note = "",
        CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        var bundle = await snapshotService.CreateCurrentSnapshotAsync(
            plan,
            startingSavings,
            clock.Today,
            FinancialSnapshotSource.Recovery,
            string.IsNullOrWhiteSpace(note)
                ? "Güncel finansal durum yenilendi"
                : note,
            cancellationToken);
        return bundle.Snapshot;
    }

    public async Task<PaymentAssignmentStrategyOverview>
        GetPaymentAssignmentStrategyOverviewAsync(
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        var history = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? clock.Today
            : plan.Settings.ProjectionAnchorDate;
        var firstProjectionSalary =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
                anchor,
                plan.Settings.SalaryDay);
        var referenceSalary = salaryPeriodCalculator
            .GetPeriod(clock.Today, plan.Settings.SalaryDay)
            .Start;
        var current = history
            .Where(x => x.EffectiveFromSalaryDate <= referenceSalary)
            .LastOrDefault() ?? history.FirstOrDefault();
        var currentThreshold = current is null
            ? referenceSalary
            : DateOnly.FromDayNumber(Math.Max(
                referenceSalary.DayNumber,
                current.EffectiveFromSalaryDate.DayNumber));
        var pending = history.FirstOrDefault(x =>
            x.EffectiveFromSalaryDate > currentThreshold);
        var firstChoice = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
            clock.Today,
            plan.Settings.SalaryDay);
        if (firstChoice <= clock.Today)
        {
            firstChoice = CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                1,
                plan.Settings.SalaryDay);
        }
        if (firstChoice < firstProjectionSalary)
        {
            firstChoice = firstProjectionSalary;
        }

        var choices = Enumerable.Range(0, 12)
            .Select(index => CalendarRules.AddMonthsKeepingDay(
                firstChoice,
                index,
                plan.Settings.SalaryDay))
            .ToArray();
        return new PaymentAssignmentStrategyOverview(
            current,
            pending,
            history,
            choices);
    }

    public async Task<PaymentStrategyChangePreview>
        PreviewPaymentAssignmentStrategyAsync(
            PaymentAssignmentMode newMode,
            DateOnly effectiveSalaryDate,
            CancellationToken cancellationToken = default)
    {
        var query = await GetProjectionPlanAsync(clock.Today, cancellationToken);
        var plan = query.Plan;
        ValidateStrategyDate(plan, effectiveSalaryDate);
        var currentMode = ResolveModeBeforeChange(plan, effectiveSalaryDate);
        var request = CreateStrategySimulationRequest(
            newMode,
            effectiveSalaryDate,
            "Maaş kullanım düzeni önizlemesi");
        var firstSalary =
            query.Boundary?.FirstUnrealizedSalaryDate ??
            salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
                plan.Settings.ProjectionAnchorDate,
                plan.Settings.SalaryDay);
        var effectiveIndex = Math.Max(
            0,
            ((effectiveSalaryDate.Year - firstSalary.Year) * 12) +
            effectiveSalaryDate.Month - firstSalary.Month);
        var result = simulationCalculator.Calculate(
            plan,
            clock.Today,
            request,
            Math.Min(60, Math.Max(12, effectiveIndex + 1)),
            firstSalary);
        var row = result.Rows.Single(x =>
            x.Scenario.PeriodStart == effectiveSalaryDate);
        return new PaymentStrategyChangePreview(
            effectiveSalaryDate,
            currentMode,
            newMode,
            row.Baseline,
            row.Scenario);
    }

    public async Task SavePaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        ValidateStrategyDate(plan, strategy.EffectiveFromSalaryDate);
        if (!Enum.IsDefined(strategy.Mode))
        {
            throw new InvalidOperationException(
                "Maaş kullanım düzeni geçersiz.");
        }

        var existing = plan.PaymentAssignmentStrategies
            .FirstOrDefault(x => x.Id == strategy.Id);
        var isHistoricalCorrection = existing is not null &&
                                     existing.EffectiveFromSalaryDate <=
                                     clock.Today;
        if (isHistoricalCorrection && !confirmedHistoricalCorrection)
        {
            throw new InvalidOperationException(
                "Geçmiş bir kararı düzeltmek önceki plan sonuçlarını değiştirir ve ayrı onay gerektirir.");
        }

        var conflicting = plan.PaymentAssignmentStrategies.FirstOrDefault(x =>
            x.EffectiveFromSalaryDate == strategy.EffectiveFromSalaryDate &&
            x.Id != strategy.Id);
        if (conflicting is not null)
        {
            if (conflicting.EffectiveFromSalaryDate <= clock.Today &&
                !confirmedHistoricalCorrection)
            {
                throw new InvalidOperationException(
                    "Bu maaş tarihindeki geçmiş kayıt yalnızca onaylı düzeltme ile değiştirilebilir.");
            }

            await store.DeletePaymentAssignmentStrategyAsync(
                conflicting.Id,
                cancellationToken);
        }

        await store.UpsertPaymentAssignmentStrategyAsync(
            strategy with
            {
                CreatedAt = existing?.CreatedAt ?? clock.UtcNow
            },
            cancellationToken);
        await CapturePlanningChangeAsync(
            "Maaş kullanım düzeni değişti",
            cancellationToken);
    }

    public async Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        bool confirmedHistoricalCorrection = false,
        CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        var strategy = plan.PaymentAssignmentStrategies
            .SingleOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Düzen kaydı bulunamadı.");
        if (plan.PaymentAssignmentStrategies.Count == 1)
        {
            throw new InvalidOperationException("İlk düzen kaydı silinemez.");
        }

        if (strategy.EffectiveFromSalaryDate <= clock.Today &&
            !confirmedHistoricalCorrection)
        {
            throw new InvalidOperationException(
                "Geçmiş düzen kaydını silmek ayrı onay gerektirir.");
        }

        var remaining = plan.PaymentAssignmentStrategies
            .Where(x => x.Id != id)
            .ToArray();
        var firstSalary = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
            plan.Settings.ProjectionAnchorDate,
            plan.Settings.SalaryDay);
        strategyResolver.ValidateHistory(
            remaining,
            plan.Settings.SalaryDay,
            firstSalary);
        await store.DeletePaymentAssignmentStrategyAsync(id, cancellationToken);
        await CapturePlanningChangeAsync(
            "Maaş kullanım düzeni değişti",
            cancellationToken);
    }

    private static SimulationRequest CreateStrategySimulationRequest(
        PaymentAssignmentMode mode,
        DateOnly effectiveSalaryDate,
        string note) => new(
            SimulationScenarioType.PaymentStrategyChange,
            note,
            0m,
            effectiveSalaryDate,
            NewPaymentAssignmentMode: mode,
            EffectiveSalaryDate: effectiveSalaryDate);

    private void ValidateStrategyDate(
        FinancialPlan plan,
        DateOnly effectiveSalaryDate)
    {
        if (!strategyResolver.IsSalaryDate(
                effectiveSalaryDate,
                plan.Settings.SalaryDay))
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir maaş tarihinde başlayabilir.");
        }
    }

    private PaymentAssignmentMode ResolveModeBeforeChange(
        FinancialPlan plan,
        DateOnly effectiveSalaryDate)
    {
        var previousSalary = CalendarRules.AddMonthsKeepingDay(
            effectiveSalaryDate,
            -1,
            plan.Settings.SalaryDay);
        return plan.PaymentAssignmentStrategies
            .Where(x => x.EffectiveFromSalaryDate <= previousSalary)
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .LastOrDefault()?.Mode ??
               plan.PaymentAssignmentStrategies
                   .OrderBy(x => x.EffectiveFromSalaryDate)
                   .First().Mode;
    }

    private static bool CanBuildProjection(FinancialPlan plan) =>
        plan.Salaries.Count > 0 &&
        plan.PaymentAssignmentStrategies.Count > 0 &&
        plan.Settings.ProjectionAnchorDate != default;

    private async Task<ProjectionQueryPlan> GetProjectionPlanAsync(
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        if (!CanBuildProjection(plan))
        {
            return new ProjectionQueryPlan(plan, null);
        }

        var history = await store.GetFinancialHistoryAsync(cancellationToken);
        var currentSnapshot = FinancialSnapshotService.LatestCurrent(history);
        if (currentSnapshot is null)
        {
            return new ProjectionQueryPlan(plan, null);
        }

        var boundary = projectionBoundaryResolver.Resolve(
            history,
            currentSnapshot,
            plan.Settings,
            asOf);
        return new ProjectionQueryPlan(
            ApplyProjectionBoundary(plan, boundary),
            boundary);
    }

    private static FinancialPlan ApplyProjectionBoundary(
        FinancialPlan plan,
        ProjectionBoundary boundary) => plan with
    {
        Settings = plan.Settings with
        {
            ProjectionStartingSavings = boundary.StartingSavings,
            ProjectionAnchorDate = boundary.ProjectionAnchorDate
        }
    };

    private sealed record ProjectionQueryPlan(
        FinancialPlan Plan,
        ProjectionBoundary? Boundary);

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
                "Gelecek hesaplamalarda sabit tutar kullanmak için 0'dan büyük bir tutar gereklidir.");
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
