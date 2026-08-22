using System.Globalization;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class SalaryPeriodDetailPresenter
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public SalaryPeriodDetailData Build(
        SalaryPeriodProjection scenario,
        SalaryPeriodProjection? baseline = null)
    {
        if (baseline is not null && baseline.Period != scenario.Period)
        {
            throw new InvalidOperationException(
                "Mevcut Plan ile Yeni Plan aynı maaş dönemine ait olmalıdır.");
        }

        var flow = BuildFlow(scenario);
        var mandatory = BuildMandatoryRows(scenario);
        var interest = BuildInterestRows(scenario);
        var cardInterest = scenario.CardPaymentStatuses
            .Where(x => x.CarryInterest > 0m)
            .Select(x => new DetailMetric(
                x.CardName,
                x.CarryInterest,
                DetailSemanticType.Interest))
            .ToArray();
        var transition = BuildTransitionRows(scenario);
        var payments = BuildPayments(scenario);
        var comparison = baseline is null
            ? []
            : BuildComparisonRows(baseline, scenario);

        return new SalaryPeriodDetailData(
            scenario.PeriodStart,
            $"{scenario.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı",
            PaymentWindowText(scenario),
            scenario.PaymentAssignmentMode == PaymentAssignmentMode.PreviousPeriod
                ? "Geçmiş dönemi kapatır"
                : "Gelecek dönemi karşılar",
            scenario.IsStrategyTransition,
            Summary(
                "GELİR",
                scenario.TotalIncome,
                DetailSemanticType.Income),
            Summary(
                "ZORUNLU",
                scenario.MandatoryOutflow,
                DetailSemanticType.Mandatory),
            Summary(
                "DÖNEM NETİ",
                scenario.EstimatedSavingsCapacity,
                scenario.EstimatedSavingsCapacity < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Savings),
            Summary(
                "DÖNEM SONU",
                scenario.EndingProjectedSavings,
                scenario.EndingProjectedSavings < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Projection),
            flow,
            scenario.HasCarryOverDeficit
                ? new DetailDeficitCallout(
                    scenario.CarryOverDeficit,
                    scenario.DeficitCoveredThisPeriod,
                    scenario.RemainingCarryOverDeficit,
                    scenario.RecoveredCarryOverDeficit)
                : null,
            mandatory,
            interest,
            cardInterest,
            transition,
            payments,
            comparison,
            BuildDebugRows(scenario));
    }

    private static DetailMetric Summary(
        string label,
        decimal amount,
        DetailSemanticType semantic) =>
        new(label, amount, semantic, DecimalPlaces: 0);

    private static IReadOnlyList<DetailMetric> BuildFlow(
        SalaryPeriodProjection row)
    {
        var result = new List<DetailMetric>
        {
            new(
                "Gelir",
                row.TotalIncome,
                DetailSemanticType.Income,
                ShowPositiveSign: true),
            new(
                "Zorunlu ödemeler",
                -row.MandatoryOutflow,
                DetailSemanticType.Mandatory),
            new(
                "Zorunlular sonrası",
                row.AvailableAfterMandatory,
                DetailSemanticType.Projection,
                IsTotal: true)
        };

        if (row.OpeningProjectedSavings > 0m)
        {
            result.Add(new DetailMetric(
                "Dönem başı durumu",
                row.OpeningProjectedSavings,
                DetailSemanticType.Savings,
                ShowPositiveSign: true));
        }
        else if (row.HasCarryOverDeficit)
        {
            result.Add(new DetailMetric(
                "Devreden açık",
                -row.CarryOverDeficit,
                DetailSemanticType.Deficit));
            result.Add(new DetailMetric(
                "Açık kapatıldıktan sonra kalan",
                row.AvailableAfterCarryOverDeficit,
                DetailSemanticType.Projection));
        }

        if (row.LivingBudget > 0m)
        {
            result.Add(new DetailMetric(
                "Tahmini yaşam gideri",
                -row.LivingBudget,
                DetailSemanticType.Expense));
        }

        if (row.PlannedLargeCashExpenses > 0m)
        {
            result.Add(new DetailMetric(
                "Planlı büyük ödeme",
                -row.PlannedLargeCashExpenses,
                DetailSemanticType.Expense));
        }

        if (row.DeficitFinancingInterest > 0m)
        {
            result.Add(new DetailMetric(
                "Faiz yükü",
                -row.DeficitFinancingInterest,
                DetailSemanticType.Interest));
        }

        result.Add(new DetailMetric(
            "Dönem sonu tahmini durum",
            row.EndingProjectedSavings,
            row.EndingProjectedSavings < 0m
                ? DetailSemanticType.Deficit
                : DetailSemanticType.Savings,
            IsTotal: true));
        return result;
    }

    private static IReadOnlyList<DetailMetric> BuildMandatoryRows(
        SalaryPeriodProjection row)
    {
        var candidates = new[]
        {
            new DetailMetric(
                "Krediler",
                row.LoanPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Kredi Kartları",
                row.CreditCardPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Geçici Ödeme Planları",
                row.TemporaryPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Taksit / Finansman",
                row.InstallmentPayments,
                DetailSemanticType.Mandatory),
            new DetailMetric(
                "Diğer Planlı Ödemeler",
                row.OtherScheduledPayments,
                DetailSemanticType.Mandatory)
        };
        return candidates.Where(x => x.Amount != 0m).ToArray();
    }

    private static IReadOnlyList<DetailMetric> BuildInterestRows(
        SalaryPeriodProjection row)
    {
        if (row.TotalInterestGenerated == 0m)
        {
            return [];
        }

        var result = new List<DetailMetric>();
        if (row.CardInterestGenerated > 0m)
        {
            result.Add(new DetailMetric(
                "Devreden kart borcu faizi",
                row.CardInterestGenerated,
                DetailSemanticType.Interest));
        }

        if (row.DeficitFinancingInterest > 0m)
        {
            result.Add(new DetailMetric(
                "Finansman açığı faiz yükü",
                row.DeficitFinancingInterest,
                DetailSemanticType.Interest));
        }

        result.Add(new DetailMetric(
            "Toplam",
            row.TotalInterestGenerated,
            DetailSemanticType.Interest,
            IsTotal: true));
        return result;
    }

    private static IReadOnlyList<DetailMetric> BuildTransitionRows(
        SalaryPeriodProjection row)
    {
        if (!row.IsStrategyTransition)
        {
            return [];
        }

        var result = new List<DetailMetric>();
        AddIfNonZero(
            result,
            "Geçmiş düzenden kapanacak",
            row.TransitionCatchUpAmount);
        AddIfNonZero(
            result,
            "Yeni dönem için ayrılacak",
            row.ForwardFundedAmount);
        if (row.MandatoryOutflow != 0m)
        {
            result.Add(new DetailMetric(
                "Toplam geçiş yükü",
                row.MandatoryOutflow,
                DetailSemanticType.Projection,
                IsTotal: true));
        }

        return result;
    }

    private static void AddIfNonZero(
        ICollection<DetailMetric> rows,
        string label,
        decimal amount)
    {
        if (amount != 0m)
        {
            rows.Add(new DetailMetric(
                label,
                amount,
                DetailSemanticType.Projection));
        }
    }

    private static IReadOnlyList<DetailPaymentRow> BuildPayments(
        SalaryPeriodProjection row)
    {
        var result = row.MandatoryItems
            .Select(ToPaymentRow)
            .ToList();
        result.AddRange(row.LargeExpenseItems.Select(expense =>
            new DetailPaymentRow(
                expense.ExactDate,
                expense.Name,
                "Planlı Büyük Ödeme",
                expense.Amount,
                DetailSemanticType.Expense,
                row.PeriodStart,
                IsEstimated: false,
                IsBeforeFundingSalary: expense.ExactDate < row.PeriodStart,
                IsUndetermined: false,
                expense.Note)));
        result.AddRange(row.CardPaymentStatuses
            .Where(x => x.Payment is null)
            .Select(card => new DetailPaymentRow(
                card.PaymentDueDate,
                card.CardName,
                "Kredi Kartı",
                null,
                DetailSemanticType.Mandatory,
                card.AssignedSalaryDate,
                IsEstimated: false,
                card.PaymentBeforeSalary,
                IsUndetermined: true,
                "Gerçek ödeme planı henüz belirlenmedi.")));
        return result
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Name)
            .ToArray();
    }

    private static DetailPaymentRow ToPaymentRow(ObligationItem item) =>
        new(
            item.DueDate,
            item.Name,
            Category(item.Type),
            item.Amount,
            DetailSemanticType.Mandatory,
            item.AssignedSalaryDate,
            item.IsEstimate,
            item.PaymentBeforeSalary,
            IsUndetermined: false,
            item.Detail);

    private static string Category(ObligationType type) => type switch
    {
        ObligationType.Loan => "Kredi",
        ObligationType.CreditCard => "Kredi Kartı",
        ObligationType.TemporaryPayment => "Geçici Plan",
        ObligationType.InstallmentPayment => "Taksit / Finansman",
        ObligationType.OtherScheduledPayment => "Planlı Ödeme",
        ObligationType.PlannedLargeExpense => "Planlı Büyük Ödeme",
        _ => "Ödeme"
    };

    private static IReadOnlyList<DetailComparisonRow> BuildComparisonRows(
        SalaryPeriodProjection baseline,
        SalaryPeriodProjection scenario)
    {
        var impact = new SimulationImpactRow(baseline, scenario);
        return
        [
            Compare(
                "Zorunlu",
                baseline.MandatoryOutflow,
                scenario.MandatoryOutflow,
                impact.MandatoryOutflowDifference,
                higherIsBetter: false),
            Compare(
                "Dönem neti",
                baseline.EstimatedSavingsCapacity,
                scenario.EstimatedSavingsCapacity,
                impact.SavingsCapacityDifference,
                higherIsBetter: true),
            Compare(
                "Faiz yükü",
                baseline.TotalInterestGenerated,
                scenario.TotalInterestGenerated,
                impact.InterestDifference,
                higherIsBetter: false),
            Compare(
                "Dönem sonu durumu",
                baseline.EndingProjectedSavings,
                scenario.EndingProjectedSavings,
                impact.ProjectedSavingsDifference,
                higherIsBetter: true)
        ];
    }

    private static DetailComparisonRow Compare(
        string label,
        decimal baseline,
        decimal scenario,
        decimal difference,
        bool higherIsBetter) =>
        new(
            label,
            baseline,
            scenario,
            difference,
            higherIsBetter);

    private static IReadOnlyList<DetailMetric> BuildDebugRows(
        SalaryPeriodProjection row)
    {
        var result = new List<DetailMetric>
        {
            new(
                "OpeningSavings",
                row.OpeningProjectedSavings,
                DetailSemanticType.Neutral),
            new(
                "CurrentContribution",
                row.CurrentPeriodNetContribution,
                DetailSemanticType.Neutral),
            new(
                "EndingBeforeDeficitInterest",
                row.EndingProjectedSavingsBeforeDeficitInterest,
                DetailSemanticType.Neutral),
            new(
                "DeficitPrincipal",
                row.DeficitPrincipal,
                DetailSemanticType.Neutral),
            new(
                "DeficitInterestRate",
                row.AppliedDeficitInterestRate,
                DetailSemanticType.Neutral,
                DisplayText:
                    $"%{(row.AppliedDeficitInterestRate * 100m).ToString("N2", TurkishCulture)}"),
            new(
                "DeficitInterest",
                row.DeficitFinancingInterest,
                DetailSemanticType.Interest),
            new(
                "FinalEndingSavings",
                row.EndingProjectedSavings,
                DetailSemanticType.Neutral)
        };
        result.AddRange(row.CardPaymentStatuses.SelectMany(card =>
            new[]
            {
                new DetailMetric(
                    $"{card.CardName} • OpeningCarry",
                    card.OpeningCarriedBalance ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • NewCharges",
                    card.NewCharges,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • Statement",
                    card.StatementBalance ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • Payment",
                    card.Payment ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • RemainingPrincipal",
                    card.CarriedPrincipalAfterPayment ?? 0m,
                    DetailSemanticType.Neutral),
                new DetailMetric(
                    $"{card.CardName} • InterestRate",
                    card.AppliedInterestRate,
                    DetailSemanticType.Neutral,
                    DisplayText:
                        $"%{(card.AppliedInterestRate * 100m).ToString("N2", TurkishCulture)}"),
                new DetailMetric(
                    $"{card.CardName} • CarryInterest",
                    card.CarryInterest,
                    DetailSemanticType.Interest),
                new DetailMetric(
                    $"{card.CardName} • NextCarry",
                    card.NextCarriedBalance ?? 0m,
                    DetailSemanticType.Neutral)
            }));
        return result;
    }

    private static string PaymentWindowText(SalaryPeriodProjection row)
    {
        var action = row.PaymentAssignmentMode ==
                     PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMMM", TurkishCulture)} – " +
               $"{row.PaymentWindowEnd.ToString("dd MMMM", TurkishCulture)} {action}";
    }
}
