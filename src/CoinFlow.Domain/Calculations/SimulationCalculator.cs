using System.Globalization;
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
    PaymentStrategyChange,
    CreditCardFullPayment
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
    DateOnly? EffectiveSalaryDate = null,
    Guid ScenarioId = default);

public sealed record SimulationImpactRow(
    SalaryPeriodProjection Baseline,
    SalaryPeriodProjection Scenario)
{
    public decimal MandatoryOutflowDifference =>
        Scenario.MandatoryOutflow - Baseline.MandatoryOutflow;
    public decimal AvailableDifference =>
        Scenario.AvailableAfterMandatory - Baseline.AvailableAfterMandatory;
    public decimal SavingsCapacityDifference =>
        Scenario.EstimatedSavingsCapacity - Baseline.EstimatedSavingsCapacity;
    public decimal ProjectedSavingsDifference =>
        Scenario.EndingProjectedSavings - Baseline.EndingProjectedSavings;
    public decimal InterestDifference =>
        Scenario.TotalInterestGenerated - Baseline.TotalInterestGenerated;
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
    string FriendlySummary)
{
    public ProjectionInterestSummary BaselineInterest =>
        ProjectionInterestSummary.From(Baseline);
    public ProjectionInterestSummary ScenarioInterest =>
        ProjectionInterestSummary.From(Scenario);
    public decimal AdditionalInterestCost =>
        ScenarioInterest.TotalInterestCost -
        BaselineInterest.TotalInterestCost;
    public decimal InterestSaving =>
        Math.Max(0m, -AdditionalInterestCost);
}

public sealed class SimulationCalculator(
    FinancialProjectionCalculator projectionCalculator,
    InstallmentScheduleCalculator installmentScheduleCalculator)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public SimulationResult Calculate(
        FinancialPlan currentPlan,
        DateOnly asOf,
        SimulationRequest request,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null) =>
        Calculate(
            currentPlan,
            asOf,
            [request],
            periodCount,
            firstSalaryDate);

    public SimulationResult Calculate(
        FinancialPlan currentPlan,
        DateOnly asOf,
        IReadOnlyList<SimulationRequest> requests,
        int periodCount = 12,
        DateOnly? firstSalaryDate = null)
    {
        Validate(requests);
        var baseline = projectionCalculator.Calculate(
            currentPlan,
            asOf,
            periodCount,
            firstSalaryDate);
        var scenarioPlan = BuildScenarioPlan(currentPlan, requests);
        var scenario = projectionCalculator.Calculate(
            scenarioPlan,
            asOf,
            periodCount,
            firstSalaryDate);
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
        var totalCost = requests.Sum(ResolveTotalCost);
        var financingCosts = requests
            .Where(x => x.Type == SimulationScenarioType.FinancingLoan)
            .Select(x => (x.TotalRepaymentAmount ?? x.Amount) - x.Amount)
            .ToArray();
        decimal? financingCost = financingCosts.Length > 0
            ? financingCosts.Sum()
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

        var interestDifference =
            ProjectionInterestSummary.From(scenario).TotalInterestCost -
            ProjectionInterestSummary.From(baseline).TotalInterestCost;
        return new SimulationResult(
            baseline,
            scenario,
            rows,
            risk,
            BuildFriendlySummary(
                risk,
                interestDifference,
                scenario[^1].EndingProjectedSavings -
                baseline[^1].EndingProjectedSavings));
    }

    public FinancialPlan BuildScenarioPlan(
        FinancialPlan plan,
        SimulationRequest request) =>
        BuildScenarioPlan(plan, [request]);

    public FinancialPlan BuildScenarioPlan(
        FinancialPlan plan,
        IReadOnlyList<SimulationRequest> requests)
    {
        Validate(requests);
        var scenarioPlan = plan;
        foreach (var request in requests.OrderBy(SortKey))
        {
            scenarioPlan = BuildScenarioPlanCore(
                scenarioPlan,
                request.ScenarioId == Guid.Empty
                    ? request with { ScenarioId = Guid.NewGuid() }
                    : request);
        }

        return scenarioPlan;
    }

    private FinancialPlan BuildScenarioPlanCore(
        FinancialPlan plan,
        SimulationRequest request) =>
        request.Type switch
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
                            Id = request.ScenarioId,
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
                            Id = request.ScenarioId,
                            Description = request.Name.Trim(),
                            Amount = request.Amount,
                            EffectiveDate = request.StartDate
                        })
                        .ToArray()
                },
            SimulationScenarioType.PaymentStrategyChange =>
                AddPaymentStrategy(plan, request),
            SimulationScenarioType.CreditCardFullPayment =>
                AddCardFullPayment(plan, request),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Type))
        };

    private static (DateOnly Date, int Type, string Name, decimal Amount, Guid Id)
        SortKey(SimulationRequest request) => (
            request.EffectiveSalaryDate ?? request.StartDate,
            (int)request.Type,
            request.Name.Trim(),
            request.Amount,
            request.ScenarioId);

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
                    Id = request.ScenarioId,
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
                Id = request.ScenarioId,
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
                "Bu plan için bir kredi kartı seçmelisin.");
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
            .Select((x, index) => new CardCharge
            {
                Id = index == 0
                    ? request.ScenarioId
                    : ChildId(request.ScenarioId, index),
                CreditCardId = card.Id,
                Description = request.PaymentCount == 1
                    ? request.Name.Trim()
                    : $"{request.Name.Trim()} ({index + 1}/{request.PaymentCount})",
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

    private static FinancialPlan AddCardFullPayment(
        FinancialPlan plan,
        SimulationRequest request)
    {
        if (request.CreditCardId is null)
        {
            throw new InvalidOperationException(
                "Tam ödeme planı için bir kredi kartı seçmelisin.");
        }

        var card = plan.CreditCards.SingleOrDefault(x =>
                       x.Id == request.CreditCardId.Value)
                   ?? throw new InvalidOperationException(
                       "Seçilen kredi kartı bulunamadı.");
        var updated = card with
        {
            PaymentPlans = card.PaymentPlans
                .Where(x => x.DueDate != request.StartDate)
                .Append(new CreditCardPaymentPlan
                {
                    Id = request.ScenarioId,
                    CreditCardId = card.Id,
                    DueDate = request.StartDate,
                    PaymentType = CreditCardPaymentType.FullStatement
                })
                .OrderBy(x => x.DueDate)
                .ToArray()
        };
        return plan with
        {
            CreditCards = plan.CreditCards
                .Select(x => x.Id == card.Id ? updated : x)
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
        return AddPaymentPlan(
            plan,
            request,
            kind,
            schedule,
            request.Amount,
            repaymentTotal);
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
            request,
            PaymentPlanKind.Recurring,
            schedule,
            request.Amount,
            request.Amount * request.PaymentCount);
    }

    private static FinancialPlan AddSinglePayment(
        FinancialPlan plan,
        SimulationRequest request) => AddPaymentPlan(
            plan,
            request,
            PaymentPlanKind.OtherScheduled,
            [new ScheduledAmount(request.StartDate, request.Amount)],
            request.Amount,
            request.Amount);

    private static FinancialPlan AddPaymentPlan(
        FinancialPlan plan,
        SimulationRequest request,
        PaymentPlanKind kind,
        IReadOnlyList<ScheduledAmount> schedule,
        decimal originalAmount,
        decimal totalRepaymentAmount)
    {
        var planId = request.ScenarioId;
        var paymentPlan = new TemporaryPaymentPlan
        {
            Id = planId,
            Name = request.Name.Trim(),
            Kind = kind,
            OriginalAmount = originalAmount,
            TotalRepaymentAmount = totalRepaymentAmount,
            Installments = schedule
                .Select((x, index) => new TemporaryPaymentInstallment
                {
                    Id = ChildId(planId, index),
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

    private static Guid ChildId(Guid parentId, int index)
    {
        var bytes = parentId.ToByteArray();
        var ordinal = BitConverter.GetBytes(index + 1);
        for (var offset = 0; offset < ordinal.Length; offset++)
        {
            bytes[12 + offset] ^= ordinal[offset];
        }

        return new Guid(bytes);
    }

    private static decimal ResolveTotalCost(SimulationRequest request) =>
        request.Type switch
        {
            SimulationScenarioType.FutureIncome or
                SimulationScenarioType.SalaryChange or
            SimulationScenarioType.PaymentStrategyChange => 0m,
            SimulationScenarioType.CreditCardFullPayment => 0m,
            SimulationScenarioType.FinancingLoan =>
                request.TotalRepaymentAmount ?? request.Amount,
            SimulationScenarioType.RecurringPayment =>
                request.Amount * request.PaymentCount,
            _ => request.Amount
        };

    private static string BuildFriendlySummary(
        SimulationRiskSummary risk,
        decimal additionalInterestCost,
        decimal endingDifference)
    {
        var parts = new List<string>();
        parts.Add(endingDifference switch
        {
            > 0m =>
                $"Bu plan 12 ay sonundaki tahmini finansal durumunu {Money(Math.Abs(endingDifference))} artırıyor.",
            < 0m =>
                $"Bu plan 12 ay sonundaki tahmini finansal durumunu {Money(Math.Abs(endingDifference))} azaltıyor.",
            _ => "Bu plan 12 ay sonundaki tahmini finansal durumu değiştirmiyor."
        });

        if (risk.FirstNegativeProjectedSavingsPeriod is SalaryPeriod negative)
        {
            parts.Add(
                $"Bu plan {PeriodMonth(negative)} döneminde finansman açığı oluşturuyor.");
            parts.Add(risk.RecoveryPeriod is SalaryPeriod recovered
                ? $"Açık {PeriodMonth(recovered)} döneminde kapanıyor."
                : "Açık gösterilen dönemlerde kapanmıyor.");
        }
        else if (risk.MaximumCarryOverDeficit > 0m)
        {
            parts.Add(risk.RecoveryPeriod is SalaryPeriod openingRecovery
                ? $"Devreden açık {PeriodMonth(openingRecovery)} döneminde kapanıyor."
                : "Devreden açık gösterilen dönemlerde kapanmıyor.");
        }
        else
        {
            parts.Add("12 aylık görünümde finansman açığı oluşmuyor.");
        }

        parts.Add(additionalInterestCost switch
        {
            > 0m =>
                $"Bu planın tahmini ek faiz yükü {Money(additionalInterestCost)}.",
            < 0m =>
                $"Bu plan mevcut plana göre {Money(Math.Abs(additionalInterestCost))} daha düşük faiz yükü oluşturuyor.",
            _ => "Bu plan tahmini faiz yükünü değiştirmiyor."
        });

        return string.Join(" ", parts);
    }

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";

    private static string PeriodMonth(SalaryPeriod period) =>
        period.Start.ToString("MMMM yyyy", TurkishCulture);

    public static void Validate(SimulationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Plan adı gereklidir.");
        }

        if (request.Type is not SimulationScenarioType.PaymentStrategyChange and
            not SimulationScenarioType.CreditCardFullPayment &&
            request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Plan tutarı 0'dan büyük olmalı.");
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

        if (request.Type == SimulationScenarioType.CreditCardFullPayment &&
            request.CreditCardId is null)
        {
            throw new InvalidOperationException(
                "Tam ödeme planı için bir kredi kartı seçmelisin.");
        }

        if (request.Type == SimulationScenarioType.FinancingLoan &&
            request.TotalRepaymentAmount is null or <= 0m)
        {
            throw new InvalidOperationException(
                "Finansman için toplam geri ödeme gereklidir.");
        }

        if (request.Type == SimulationScenarioType.FinancingLoan &&
            request.FirstPaymentDate is null)
        {
            throw new InvalidOperationException(
                "Finansman için ilk ödeme tarihi gereklidir.");
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

    public static void Validate(IReadOnlyList<SimulationRequest> requests)
    {
        if (requests.Count == 0)
        {
            throw new InvalidOperationException(
                "Simülasyon için en az bir koşul eklemelisin.");
        }

        foreach (var request in requests)
        {
            Validate(request);
        }

        var conflictingSalary = requests
            .Where(x => x.Type == SimulationScenarioType.SalaryChange)
            .GroupBy(x => x.StartDate)
            .FirstOrDefault(x => x.Count() > 1);
        if (conflictingSalary is not null)
        {
            throw new InvalidOperationException(
                $"{conflictingSalary.Key.ToString("dd MMMM yyyy", TurkishCulture)} için iki farklı maaş değişikliği var. Simülasyonu çalıştırmadan önce birini düzenle veya kaldır.");
        }

        var conflictingStrategy = requests
            .Where(x => x.Type == SimulationScenarioType.PaymentStrategyChange)
            .GroupBy(x => x.EffectiveSalaryDate ?? x.StartDate)
            .FirstOrDefault(x => x.Count() > 1);
        if (conflictingStrategy is not null)
        {
            throw new InvalidOperationException(
                $"{conflictingStrategy.Key.ToString("dd MMMM yyyy", TurkishCulture)} maaşı için iki farklı kullanım düzeni değişikliği var. Simülasyonu çalıştırmadan önce birini düzenle veya kaldır.");
        }
    }
}
