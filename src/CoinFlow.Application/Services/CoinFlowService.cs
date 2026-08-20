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
    SalaryPeriodCalculator salaryPeriodCalculator)
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

        return new FinancialPlan
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
    }

    public async Task<DashboardSnapshot?> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var plan = await GetFinancialPlanAsync(cancellationToken);
        if (!CanBuildProjection(plan))
        {
            return null;
        }

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
        if (!CanBuildProjection(plan))
        {
            return [];
        }

        return projectionService.BuildFuturePeriods(plan, date, periodCount);
    }

    public async Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? clock.Today;
        var plan = await GetFinancialPlanAsync(cancellationToken);
        if (!CanBuildProjection(plan))
        {
            throw new InvalidOperationException(
                "Simülasyon yapabilmek için önce maaşını ve maaş kullanım düzenini oluştur.");
        }

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

    public async Task<SimulationApplyResult> ApplySimulationAsync(
        SimulationRequest request,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "Plan, açık kullanıcı onayı olmadan uygulanamaz.");
        }

        SimulationCalculator.Validate(request);
        if (request.ScenarioId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Uygulanacak simülasyon kimliği bulunamadı. Planı yeniden simüle edin.");
        }

        var current = await GetFinancialPlanAsync(cancellationToken);
        var existingResult = FindAppliedSimulation(current, request);
        if (existingResult is not null)
        {
            return existingResult with { AlreadyApplied = true };
        }

        if (request.Type == SimulationScenarioType.SalaryChange &&
            current.Salaries.Any(x => x.EffectiveDate == request.StartDate))
        {
            throw new InvalidOperationException(
                "Bu tarihte zaten bir maaş kaydı var. Geçmişi korumak için farklı bir geçerlilik tarihi seçin.");
        }

        var strategyDate = request.EffectiveSalaryDate ?? request.StartDate;
        if (request.Type == SimulationScenarioType.PaymentStrategyChange &&
            current.PaymentAssignmentStrategies.Any(x =>
                x.EffectiveFromSalaryDate == strategyDate))
        {
            throw new InvalidOperationException(
                "Bu maaş tarihinde zaten bir kullanım düzeni var. Mevcut history kaydı değiştirilemez.");
        }

        var scenario = simulationCalculator.BuildScenarioPlan(current, request);
        SimulationApplyResult result;

        switch (request.Type)
        {
            case SimulationScenarioType.CashPurchase:
                var expense = scenario.PlannedLargeExpenses.Single(x =>
                    x.Id == request.ScenarioId);
                await store.UpsertPlannedLargeExpenseAsync(
                    expense,
                    cancellationToken);
                result = AppliedResult(
                    request,
                    expense.Id,
                    SimulationApplyDestination.Payments,
                    "Plan ödeme planına eklendi.");
                break;
            case SimulationScenarioType.CreditCardSinglePayment:
            case SimulationScenarioType.CreditCardInstallmentPurchase:
            case SimulationScenarioType.CreditCardFullPayment:
                var changedCard = scenario.CreditCards.Single(x =>
                    x.Id == request.CreditCardId);
                await store.UpsertCreditCardAsync(changedCard, cancellationToken);
                result = AppliedResult(
                    request,
                    changedCard.Id,
                    SimulationApplyDestination.CreditCard,
                    $"Plan {changedCard.Bank} {changedCard.Name} kartına eklendi.");
                break;
            case SimulationScenarioType.FinancingLoan:
            case SimulationScenarioType.CashDebt:
            case SimulationScenarioType.FutureOneTimePayment:
            case SimulationScenarioType.RecurringPayment:
                var paymentPlan = scenario.PaymentPlans.Single(x =>
                    x.Id == request.ScenarioId);
                await store.UpsertPaymentPlanAsync(
                    paymentPlan,
                    cancellationToken);
                result = AppliedResult(
                    request,
                    paymentPlan.Id,
                    SimulationApplyDestination.Payments,
                    "Plan ödeme planına eklendi.");
                break;
            case SimulationScenarioType.FutureIncome:
                var income = scenario.OtherIncomes.Single(x =>
                    x.Id == request.ScenarioId);
                await store.UpsertOtherIncomeAsync(
                    income,
                    cancellationToken);
                result = AppliedResult(
                    request,
                    income.Id,
                    SimulationApplyDestination.Income,
                    "Plan gelir planına eklendi.");
                break;
            case SimulationScenarioType.SalaryChange:
                var salary = scenario.Salaries.Single(x =>
                    x.Id == request.ScenarioId);
                await store.UpsertSalaryAsync(
                    salary,
                    cancellationToken);
                result = AppliedResult(
                    request,
                    salary.Id,
                    SimulationApplyDestination.SalaryHistory,
                    "Yeni maaş salary history'ye eklendi.");
                break;
            case SimulationScenarioType.PaymentStrategyChange:
                var strategy = scenario.PaymentAssignmentStrategies.Single(x =>
                    x.Id == request.ScenarioId);
                await SavePaymentAssignmentStrategyAsync(
                    strategy,
                    confirmedHistoricalCorrection: false,
                    cancellationToken);
                result = AppliedResult(
                    request,
                    strategy.Id,
                    SimulationApplyDestination.Settings,
                    "Yeni maaş kullanım düzeni history'ye eklendi.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Type));
        }

        return result;
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
                    "Plan daha önce ödeme planına eklendi."),
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
                    "Plan daha önce ödeme planına eklendi."),
            SimulationScenarioType.FutureIncome
                when plan.OtherIncomes.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Income,
                    "Plan daha önce gelir planına eklendi."),
            SimulationScenarioType.SalaryChange
                when plan.Salaries.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.SalaryHistory,
                    "Maaş planı daha önce history'ye eklendi."),
            SimulationScenarioType.PaymentStrategyChange
                when plan.PaymentAssignmentStrategies.Any(x => x.Id == entityId) =>
                AppliedResult(request, entityId, SimulationApplyDestination.Settings,
                    "Düzen değişikliği daha önce history'ye eklendi."),
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
        await store.SaveSettingsAsync(settings, cancellationToken);
        if (settings.SalaryDay != plan.Settings.SalaryDay)
        {
            foreach (var strategy in plan.PaymentAssignmentStrategies)
            {
                var oldDate = strategy.EffectiveFromSalaryDate;
                await store.UpsertPaymentAssignmentStrategyAsync(
                    strategy with
                    {
                        EffectiveFromSalaryDate = CalendarRules.ResolveDay(
                            oldDate.Year,
                            oldDate.Month,
                            settings.SalaryDay)
                    },
                    cancellationToken);
            }
        }
    }

    public async Task<PaymentAssignmentStrategyOverview>
        GetPaymentAssignmentStrategyOverviewAsync(
            CancellationToken cancellationToken = default)
    {
        var plan = await GetFinancialPlanAsync(cancellationToken);
        var history = plan.PaymentAssignmentStrategies
            .OrderBy(x => x.EffectiveFromSalaryDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();
        var anchor = plan.Settings.ProjectionAnchorDate == default
            ? clock.Today
            : plan.Settings.ProjectionAnchorDate;
        var firstProjectionSalary = salaryPeriodCalculator
            .GetFirstSalaryOnOrAfter(anchor, plan.Settings.SalaryDay);
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
        var plan = await GetFinancialPlanAsync(cancellationToken);
        ValidateStrategyDate(plan, effectiveSalaryDate);
        var currentMode = ResolveModeBeforeChange(plan, effectiveSalaryDate);
        var request = CreateStrategySimulationRequest(
            newMode,
            effectiveSalaryDate,
            "Maaş kullanım düzeni önizlemesi");
        var firstSalary = salaryPeriodCalculator.GetFirstSalaryOnOrAfter(
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
            Math.Min(60, Math.Max(12, effectiveIndex + 1)));
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
                "Geçmiş kayıt düzeltmesi projection geçmişini değiştirir ve ayrı onay gerektirir.");
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
