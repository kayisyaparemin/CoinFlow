using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public enum SimulationScenarioType
{
    CashPurchase,
    CreditCardSinglePayment,
    CreditCardInstallmentPurchase,
    FinancingLoan,
    CashDebt,
    FutureOneTimePayment,
    RecurringPayment,
    FutureIncome,
    SalaryChange,
    PaymentStrategyChange
}

public sealed record SimulationRequest(
    SimulationScenarioType Type,
    string Name,
    decimal Amount,
    DateOnly StartDate,
    int PaymentCount = 1,
    DateOnly? FirstPaymentDate = null,
    Guid? CreditCardId = null,
    decimal? TotalRepaymentAmount = null,
    PaymentAssignmentMode? NewPaymentAssignmentMode = null,
    DateOnly? EffectiveSalaryDate = null);

public sealed record SimulationImpactRow(
    SalaryPeriodProjection Baseline,
    SalaryPeriodProjection Scenario)
{
    public decimal AvailableDifference =>
        Scenario.AvailableAfterMandatory - Baseline.AvailableAfterMandatory;
    public decimal SavingsCapacityDifference =>
        Scenario.EstimatedSavingsCapacity - Baseline.EstimatedSavingsCapacity;
    public decimal ProjectedSavingsDifference =>
        Scenario.EndingProjectedSavings - Baseline.EndingProjectedSavings;
}

public sealed record SimulationRiskSummary(
    decimal LowestAvailableAfterMandatory,
    decimal LowestSavingsCapacity,
    decimal LowestProjectedSavings,
    SalaryPeriod LowestPeriod,
    SalaryPeriod? FirstNegativeSavingsCapacityPeriod,
    SalaryPeriod? FirstNegativeProjectedSavingsPeriod,
    decimal MaximumCarryOverDeficit,
    SalaryPeriod? RecoveryPeriod,
    decimal EndingProjectedSavings,
    decimal TotalScenarioCost,
    decimal? FinancingCost)
{
    public SalaryPeriod? FirstDeficitPeriod =>
        FirstNegativeProjectedSavingsPeriod;
}

public sealed record SimulationResult(
    IReadOnlyList<SalaryPeriodProjection> Baseline,
    IReadOnlyList<SalaryPeriodProjection> Scenario,
    IReadOnlyList<SimulationImpactRow> Rows,
    SimulationRiskSummary Risk,
    string FriendlySummary);

public sealed class SimulationCalculator(
    FinancialProjectionCalculator projectionCalculator,
    InstallmentScheduleCalculator installmentScheduleCalculator)
{
    public SimulationResult Calculate(
        FinancialPlan currentPlan,
        DateOnly asOf,
        SimulationRequest request,
        int periodCount = 12)
    {
        Validate(request);
        var baseline = projectionCalculator.Calculate(
            currentPlan,
            asOf,
            periodCount);
        var scenarioPlan = BuildScenarioPlan(currentPlan, request);
        var scenario = projectionCalculator.Calculate(
            scenarioPlan,
            asOf,
            periodCount);
        var rows = baseline
            .Zip(scenario, (current, planned) =>
                new SimulationImpactRow(current, planned))
            .ToArray();
        var lowest = scenario
            .OrderBy(x => x.EndingProjectedSavings)
            .ThenBy(x => x.PeriodStart)
            .First();
        var firstNegativeCapacity = scenario
            .FirstOrDefault(x => x.EstimatedSavingsCapacity < 0m);
        var firstNegativeSavings = scenario
            .FirstOrDefault(x => x.EndingProjectedSavings < 0m);
        var maximumCarryOverDeficit = scenario
            .Select(x => x.CarryOverDeficit)
            .Append(scenario[^1].RemainingCarryOverDeficit)
            .Max();
        var recovery = scenario.FirstOrDefault(x =>
            x.HasCarryOverDeficit && x.EndingProjectedSavings >= 0m);
        var totalCost = ResolveTotalCost(request);
        decimal? financingCost = request.Type == SimulationScenarioType.FinancingLoan
            ? (request.TotalRepaymentAmount ?? request.Amount) - request.Amount
            : null;
        var risk = new SimulationRiskSummary(
            scenario.Min(x => x.AvailableAfterMandatory),
            scenario.Min(x => x.EstimatedSavingsCapacity),
            scenario.Min(x => x.EndingProjectedSavings),
            lowest.Period,
            firstNegativeCapacity?.Period,
            firstNegativeSavings?.Period,
            maximumCarryOverDeficit,
            recovery?.Period,
            scenario[^1].EndingProjectedSavings,
            totalCost,
            financingCost);

        return new SimulationResult(
            baseline,
            scenario,
            rows,
            risk,
            BuildFriendlySummary(risk));
    }

    public FinancialPlan BuildScenarioPlan(
        FinancialPlan plan,
        SimulationRequest request)
    {
        Validate(request);
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase =>
                AddLargeExpense(plan, request),
            SimulationScenarioType.CreditCardSinglePayment =>
                AddCardPurchase(plan, request with { PaymentCount = 1 }),
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                AddCardPurchase(plan, request),
            SimulationScenarioType.FinancingLoan =>
                AddInstallmentPlan(
                    plan,
                    request,
                    request.TotalRepaymentAmount ?? request.Amount,
                    PaymentPlanKind.Installment),
            SimulationScenarioType.CashDebt =>
                AddInstallmentPlan(
                    plan,
                    request,
                    request.Amount,
                    PaymentPlanKind.OtherScheduled),
            SimulationScenarioType.FutureOneTimePayment =>
                AddSinglePayment(plan, request),
            SimulationScenarioType.RecurringPayment =>
                AddRecurringPayment(plan, request),
            SimulationScenarioType.FutureIncome =>
                plan with
                {
                    OtherIncomes = plan.OtherIncomes
                        .Append(new OneTimeIncome
                        {
                            Description = request.Name.Trim(),
                            Amount = request.Amount,
                            ExactDate = request.StartDate
                        })
                        .ToArray()
                },
            SimulationScenarioType.SalaryChange =>
                plan with
                {
                    Salaries = plan.Salaries
                        .Where(x => x.EffectiveDate != request.StartDate)
                        .Append(new SalaryScheduleEntry
                        {
                            Description = request.Name.Trim(),
                            Amount = request.Amount,
                            EffectiveDate = request.StartDate
                        })
                        .ToArray()
                },
            SimulationScenarioType.PaymentStrategyChange =>
                AddPaymentStrategy(plan, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Type))
        };
    }

    private static FinancialPlan AddPaymentStrategy(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var effectiveDate = request.EffectiveSalaryDate ?? request.StartDate;
        if (CalendarRules.ResolveDay(
                effectiveDate.Year,
                effectiveDate.Month,
                plan.Settings.SalaryDay) != effectiveDate)
        {
            throw new InvalidOperationException(
                "Düzen değişikliği yalnızca bir maaş tarihinde başlayabilir.");
        }

        var mode = request.NewPaymentAssignmentMode ??
                   throw new InvalidOperationException(
                       "Yeni maaş kullanım düzeni seçilmelidir.");
        return plan with
        {
            PaymentAssignmentStrategies = plan.PaymentAssignmentStrategies
                .Where(x => x.EffectiveFromSalaryDate != effectiveDate)
                .Append(new PaymentAssignmentStrategy
                {
                    Mode = mode,
                    EffectiveFromSalaryDate = effectiveDate,
                    Note = request.Name.Trim()
                })
                .OrderBy(x => x.EffectiveFromSalaryDate)
                .ToArray()
        };
    }

    private static FinancialPlan AddLargeExpense(
        FinancialPlan plan,
        SimulationRequest request) => plan with
    {
        PlannedLargeExpenses = plan.PlannedLargeExpenses
            .Append(new PlannedLargeExpense
            {
                Name = request.Name.Trim(),
                Amount = request.Amount,
                ExactDate = request.StartDate,
                Status = PlannedExpenseStatus.Planned
            })
            .ToArray()
    };

    private FinancialPlan AddCardPurchase(
        FinancialPlan plan,
        SimulationRequest request)
    {
        if (request.CreditCardId is null)
        {
            throw new InvalidOperationException(
                "Kredi kartı senaryosu için kart seçilmelidir.");
        }

        var card = plan.CreditCards
            .SingleOrDefault(x => x.Id == request.CreditCardId.Value)
            ?? throw new InvalidOperationException("Seçilen kredi kartı bulunamadı.");
        var availableLimit = card.Limit - card.KnownTotalDebt;
        if (card.Limit > 0m && request.Amount > availableLimit)
        {
            throw new InvalidOperationException(
                "Kartın bilinen kullanılabilir limiti bu plan için yetersiz.");
        }

        var charges = installmentScheduleCalculator
            .Split(request.Amount, request.PaymentCount, request.StartDate)
            .Select(x => new CardCharge
            {
                CreditCardId = card.Id,
                Description = request.Name.Trim(),
                PostingDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        var updatedCard = card with
        {
            Charges = card.Charges.Concat(charges).ToArray()
        };

        return plan with
        {
            CreditCards = plan.CreditCards
                .Select(x => x.Id == card.Id ? updatedCard : x)
                .ToArray()
        };
    }

    private FinancialPlan AddInstallmentPlan(
        FinancialPlan plan,
        SimulationRequest request,
        decimal repaymentTotal,
        PaymentPlanKind kind)
    {
        if (repaymentTotal < request.Amount)
        {
            throw new InvalidOperationException(
                "Toplam geri ödeme ana tutardan düşük olamaz.");
        }

        var firstPaymentDate = request.FirstPaymentDate ?? request.StartDate;
        var schedule = installmentScheduleCalculator.Split(
            repaymentTotal,
            request.PaymentCount,
            firstPaymentDate);
        return AddPaymentPlan(plan, request.Name, kind, schedule);
    }

    private static FinancialPlan AddRecurringPayment(
        FinancialPlan plan,
        SimulationRequest request)
    {
        var firstPaymentDate = request.FirstPaymentDate ?? request.StartDate;
        var schedule = Enumerable.Range(0, request.PaymentCount)
            .Select(index => new ScheduledAmount(
                CalendarRules.AddMonthsKeepingDay(
                    firstPaymentDate,
                    index,
                    firstPaymentDate.Day),
                request.Amount))
            .ToArray();
        return AddPaymentPlan(
            plan,
            request.Name,
            PaymentPlanKind.Recurring,
            schedule);
    }

    private static FinancialPlan AddSinglePayment(
        FinancialPlan plan,
        SimulationRequest request) => AddPaymentPlan(
            plan,
            request.Name,
            PaymentPlanKind.OtherScheduled,
            [new ScheduledAmount(request.StartDate, request.Amount)]);

    private static FinancialPlan AddPaymentPlan(
        FinancialPlan plan,
        string name,
        PaymentPlanKind kind,
        IReadOnlyList<ScheduledAmount> schedule)
    {
        var planId = Guid.NewGuid();
        var paymentPlan = new TemporaryPaymentPlan
        {
            Id = planId,
            Name = name.Trim(),
            Kind = kind,
            Installments = schedule
                .Select(x => new TemporaryPaymentInstallment
                {
                    PlanId = planId,
                    DueDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray()
        };
        return plan with
        {
            PaymentPlans = plan.PaymentPlans.Append(paymentPlan).ToArray()
        };
    }

    private static decimal ResolveTotalCost(SimulationRequest request) =>
        request.Type switch
        {
            SimulationScenarioType.FutureIncome or
                SimulationScenarioType.SalaryChange or
                SimulationScenarioType.PaymentStrategyChange => 0m,
            SimulationScenarioType.FinancingLoan =>
                request.TotalRepaymentAmount ?? request.Amount,
            SimulationScenarioType.RecurringPayment =>
                request.Amount * request.PaymentCount,
            _ => request.Amount
        };

    private static string BuildFriendlySummary(SimulationRiskSummary risk)
    {
        if (risk.FirstNegativeProjectedSavingsPeriod is SalaryPeriod negative)
        {
            var recovery = risk.RecoveryPeriod is SalaryPeriod recovered
                ? $" Açık {recovered.Start:dd.MM.yyyy} maaş döneminde kapanıyor."
                : " Açık gösterilen dönemlerde kapanmıyor.";
            return $"İlk negatif tahmini birikim dönemi: {negative.Start:dd.MM.yyyy}–{negative.End:dd.MM.yyyy}.{recovery}";
        }

        if (risk.MaximumCarryOverDeficit > 0m &&
            risk.RecoveryPeriod is SalaryPeriod openingRecovery)
        {
            return $"Devreden finansman açığı {openingRecovery.Start:dd.MM.yyyy} maaş döneminde kapanıyor.";
        }

        if (risk.FirstNegativeSavingsCapacityPeriod is SalaryPeriod capacity)
        {
            return $"Bu plan {capacity.Start:dd.MM.yyyy}–{capacity.End:dd.MM.yyyy} döneminde yaşam bütçesi sonrası açık oluşturuyor.";
        }

        return "Bu plan, gösterilen dönemlerde tahmini birikimi negatife düşürmüyor.";
    }

    private static void Validate(SimulationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Senaryo adı gereklidir.");
        }

        if (request.Type != SimulationScenarioType.PaymentStrategyChange &&
            request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Senaryo tutarı sıfırdan büyük olmalıdır.");
        }

        if (request.Type == SimulationScenarioType.PaymentStrategyChange &&
            (request.NewPaymentAssignmentMode is null ||
             request.EffectiveSalaryDate is null))
        {
            throw new InvalidOperationException(
                "Yeni düzen ve geçerli maaş tarihi seçilmelidir.");
        }

        var needsCount = request.Type is
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        if (needsCount && request.PaymentCount is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Ödeme sayısı 1 ile 120 arasında olmalıdır.");
        }

        if (request.Type == SimulationScenarioType.FinancingLoan &&
            request.TotalRepaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Finansman için toplam geri ödeme gereklidir.");
        }

        if (request.FirstPaymentDate is DateOnly firstPayment &&
            firstPayment < request.StartDate &&
            request.Type is SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment)
        {
            throw new InvalidOperationException(
                "İlk ödeme tarihi başlangıç tarihinden önce olamaz.");
        }
    }
}
