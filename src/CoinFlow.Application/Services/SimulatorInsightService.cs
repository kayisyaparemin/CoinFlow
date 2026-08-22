using System.Globalization;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class SimulatorInsightService
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public SimulatorProjectionSummary Build(
        IReadOnlyList<SalaryPeriodProjection> scenario)
    {
        if (scenario.Count == 0)
        {
            throw new InvalidOperationException(
                "Simülasyon sonucu bulunamadı.");
        }

        var periodViews = scenario
            .Select(row => new SimulatorPeriodView(
                row,
                SalaryText(row.PeriodStart),
                AssignmentText(row),
                row.OpeningProjectedSavings,
                row.TotalIncome,
                SimulatorProjectionMath.PeriodNeed(row),
                row.TotalIncome - SimulatorProjectionMath.PeriodNeed(row),
                row.EndingProjectedSavings,
                SimulatorProjectionMath.BuildNeedBreakdown(row),
                []))
            .ToArray();

        var highestNeed = periodViews
            .OrderByDescending(x => x.NeedTotal)
            .ThenBy(x => x.Projection.PeriodStart)
            .First();
        var lowestEnding = periodViews
            .OrderBy(x => x.EndingSituation)
            .ThenBy(x => x.Projection.PeriodStart)
            .First();
        var firstIncomeInsufficient = periodViews.FirstOrDefault(x =>
            x.IncomeCoverage < 0m);
        var firstDeficit = periodViews.FirstOrDefault(x =>
            x.EndingSituation < 0m);
        var deficitRecovery = firstDeficit is null
            ? null
            : periodViews
                .SkipWhile(x => x.Projection.PeriodStart <=
                                firstDeficit.Projection.PeriodStart)
                .FirstOrDefault(x => x.EndingSituation >= 0m);
        var burdenRelief = FindBurdenRelief(periodViews, lowestEnding);

        var withChips = periodViews
            .Select(period => period with
            {
                InsightChips = BuildChips(
                    period,
                    highestNeed,
                    lowestEnding,
                    firstIncomeInsufficient,
                    firstDeficit,
                    deficitRecovery,
                    burdenRelief)
            })
            .ToArray();

        var summary = BuildNarrative(
            highestNeed,
            lowestEnding,
            firstIncomeInsufficient,
            firstDeficit,
            deficitRecovery,
            burdenRelief,
            periodViews[^1]);

        return new SimulatorProjectionSummary(
            summary,
            BuildKeyMetrics(
                highestNeed,
                lowestEnding,
                firstIncomeInsufficient,
                firstDeficit,
                deficitRecovery,
                periodViews[^1]),
            withChips,
            highestNeed,
            lowestEnding,
            firstIncomeInsufficient,
            firstDeficit,
            deficitRecovery,
            burdenRelief,
            periodViews[^1].EndingSituation);
    }

    private static IReadOnlyList<string> BuildNarrative(
        SimulatorPeriodView highestNeed,
        SimulatorPeriodView lowestEnding,
        SimulatorPeriodView? firstIncomeInsufficient,
        SimulatorPeriodView? firstDeficit,
        SimulatorPeriodView? deficitRecovery,
        SimulatorPeriodView? burdenRelief,
        SimulatorPeriodView finalPeriod)
    {
        var sentences = new List<string>();

        if (firstDeficit is not null)
        {
            sentences.Add(
                $"{Month(firstDeficit)} döneminde finansman açığı oluşuyor; dönem sonu tahmini açık {Money(Math.Abs(firstDeficit.EndingSituation))}.");
            sentences.Add(deficitRecovery is not null
                ? $"Açığın {Month(deficitRecovery)} döneminde kapanması bekleniyor."
                : "Açık 12 aylık görünüm içinde kapanmıyor.");
        }
        else
        {
            sentences.Add("12 aylık görünümde finansman açığı oluşmuyor.");
        }

        if (firstIncomeInsufficient is not null &&
            !SamePeriod(firstIncomeInsufficient, firstDeficit))
        {
            sentences.Add(
                $"{Month(firstIncomeInsufficient)} döneminde dönem gelirlerin toplam ihtiyacını tek başına karşılamıyor; yaklaşık {Money(Math.Abs(firstIncomeInsufficient.IncomeCoverage))} dönem başı durumundan kullanılıyor.");
        }

        if (lowestEnding.EndingSituation < 0m)
        {
            if (!SamePeriod(lowestEnding, firstDeficit))
            {
                sentences.Add(
                    $"En sıkışık dönem {Month(lowestEnding)}; yaklaşık {Money(Math.Abs(lowestEnding.EndingSituation))} finansman açığı oluşuyor.");
            }
        }
        else
        {
            sentences.Add(
                $"En sıkışık dönem {Month(lowestEnding)}; dönem sonu tahmini durumun {Money(lowestEnding.EndingSituation)}.");
        }

        if (!SamePeriod(highestNeed, lowestEnding) &&
            highestNeed.NeedTotal > 0m)
        {
            sentences.Add(
                $"Önündeki 12 ayda en yüksek toplam ihtiyaç {Month(highestNeed)} döneminde: {Money(highestNeed.NeedTotal)}.");
        }

        if (burdenRelief is not null)
        {
            sentences.Add(
                $"{Month(burdenRelief)} döneminde ödeme yükü belirgin azalıyor ve finansal durum yeniden toparlanmaya başlıyor.");
        }

        if (sentences.Count < 2)
        {
            sentences.Add(
                $"12 ay sonu tahmini durumun {Money(finalPeriod.EndingSituation)}.");
        }

        return sentences
            .Distinct()
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<SimulatorSummaryMetric> BuildKeyMetrics(
        SimulatorPeriodView highestNeed,
        SimulatorPeriodView lowestEnding,
        SimulatorPeriodView? firstIncomeInsufficient,
        SimulatorPeriodView? firstDeficit,
        SimulatorPeriodView? deficitRecovery,
        SimulatorPeriodView finalPeriod)
    {
        var metrics = new List<SimulatorSummaryMetric>
        {
            new(
                "En sıkışık dönem",
                Month(lowestEnding),
                lowestEnding.EndingSituation < 0m
                    ? $"{Money(Math.Abs(lowestEnding.EndingSituation))} açık"
                    : Money(lowestEnding.EndingSituation),
                lowestEnding.EndingSituation < 0m
                    ? DetailSemanticType.Deficit
                    : DetailSemanticType.Projection),
            new(
                "En yüksek ihtiyaç",
                Month(highestNeed),
                Money(highestNeed.NeedTotal),
                DetailSemanticType.Mandatory)
        };

        if (firstIncomeInsufficient is not null)
        {
            metrics.Add(new SimulatorSummaryMetric(
                "Gelirin ilk yetmediği dönem",
                Month(firstIncomeInsufficient),
                Money(Math.Abs(firstIncomeInsufficient.IncomeCoverage)),
                DetailSemanticType.Projection));
        }

        metrics.Add(firstDeficit is null
            ? new SimulatorSummaryMetric(
                "Finansman açığı",
                "Yok",
                "12 ay içinde oluşmuyor",
                DetailSemanticType.Savings)
            : new SimulatorSummaryMetric(
                "Finansman açığı",
                Month(firstDeficit),
                Money(Math.Abs(firstDeficit.EndingSituation)),
                DetailSemanticType.Deficit));

        if (deficitRecovery is not null)
        {
            metrics.Add(new SimulatorSummaryMetric(
                "Açığın kapanışı",
                Month(deficitRecovery),
                Money(deficitRecovery.EndingSituation),
                DetailSemanticType.Savings));
        }

        metrics.Add(new SimulatorSummaryMetric(
            "12 ay sonu",
            Money(finalPeriod.EndingSituation),
            string.Empty,
            finalPeriod.EndingSituation < 0m
                ? DetailSemanticType.Deficit
                : DetailSemanticType.Projection));

        return metrics;
    }

    private static SimulatorPeriodView? FindBurdenRelief(
        IReadOnlyList<SimulatorPeriodView> periods,
        SimulatorPeriodView lowestEnding)
    {
        var lowestIndex = periods.ToList().FindIndex(x =>
            SamePeriod(x, lowestEnding));
        if (lowestIndex < 0)
        {
            return null;
        }

        // Recovery is intentionally conservative: a period must follow the
        // tightest point, reduce total need by at least 15% and 10.000 TL,
        // and start a non-decreasing ending-situation trend.
        for (var index = lowestIndex + 1; index < periods.Count; index++)
        {
            var previous = periods[index - 1];
            var current = periods[index];
            var needDrop = previous.NeedTotal - current.NeedTotal;
            var materialDrop = needDrop >= 10_000m &&
                               previous.NeedTotal > 0m &&
                               needDrop / previous.NeedTotal >= 0.15m;
            var trendContinues = index == periods.Count - 1 ||
                                 periods[index + 1].EndingSituation >=
                                 current.EndingSituation;
            if (materialDrop &&
                current.EndingSituation > previous.EndingSituation &&
                trendContinues)
            {
                return current;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildChips(
        SimulatorPeriodView period,
        SimulatorPeriodView highestNeed,
        SimulatorPeriodView lowestEnding,
        SimulatorPeriodView? firstIncomeInsufficient,
        SimulatorPeriodView? firstDeficit,
        SimulatorPeriodView? deficitRecovery,
        SimulatorPeriodView? burdenRelief)
    {
        var candidates = new List<(int Priority, string Text)>();
        if (SamePeriod(period, firstDeficit))
        {
            candidates.Add((0, "Finansman açığı oluşuyor"));
        }

        if (SamePeriod(period, deficitRecovery))
        {
            candidates.Add((1, "Açık bu ay kapanıyor"));
        }

        if (SamePeriod(period, firstIncomeInsufficient))
        {
            candidates.Add((2, "Gelirler bu ay tek başına yetmiyor"));
        }

        if (SamePeriod(period, lowestEnding))
        {
            candidates.Add((3, "En düşük dönem sonu"));
        }

        if (SamePeriod(period, highestNeed))
        {
            candidates.Add((4, "En yüksek ihtiyaç"));
        }

        if (SamePeriod(period, burdenRelief))
        {
            candidates.Add((5, "Ödeme yükü belirgin azalıyor"));
        }

        return candidates
            .OrderBy(x => x.Priority)
            .Select(x => x.Text)
            .Distinct()
            .Take(2)
            .ToArray();
    }

    private static string SalaryText(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";

    private static string AssignmentText(SalaryPeriodProjection row)
    {
        var action = row.PaymentAssignmentMode ==
                     PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

    private static string Month(SimulatorPeriodView? period) =>
        period?.Projection.PeriodStart.ToString("MMMM yyyy", TurkishCulture) ??
        string.Empty;

    private static bool SamePeriod(
        SimulatorPeriodView? left,
        SimulatorPeriodView? right) =>
        left is not null &&
        right is not null &&
        left.Projection.PeriodStart == right.Projection.PeriodStart;

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";
}

public static class SimulatorProjectionMath
{
    public static decimal PeriodNeed(SalaryPeriodProjection row) =>
        row.MandatoryOutflow +
        row.LivingBudget +
        row.PlannedLargeCashExpenses +
        row.DeficitFinancingInterest;

    public static IReadOnlyList<DetailMetric> BuildNeedBreakdown(
        SalaryPeriodProjection row)
    {
        var rows = new List<DetailMetric>();
        AddIfNonZero(rows, "Krediler", row.LoanPayments);
        AddIfNonZero(rows, "Kredi kartları", row.CreditCardPayments);
        AddIfNonZero(rows, "Geçici ödemeler", row.TemporaryPayments);
        AddIfNonZero(rows, "Taksit / finansman", row.InstallmentPayments);
        AddIfNonZero(
            rows,
            "Planlı nakit ödemeler",
            row.PlannedLargeCashExpenses,
            DetailSemanticType.Expense);
        AddIfNonZero(rows, "Diğer planlı ödemeler", row.OtherScheduledPayments);
        AddIfNonZero(
            rows,
            "Tahmini yaşam gideri",
            row.LivingBudget,
            DetailSemanticType.Expense);
        AddIfNonZero(
            rows,
            "Finansman açığı faizi",
            row.DeficitFinancingInterest,
            DetailSemanticType.Interest);
        rows.Add(new DetailMetric(
            "Toplam",
            PeriodNeed(row),
            DetailSemanticType.Mandatory,
            IsTotal: true));
        return rows;
    }

    private static void AddIfNonZero(
        ICollection<DetailMetric> rows,
        string label,
        decimal amount,
        DetailSemanticType semantic = DetailSemanticType.Mandatory)
    {
        if (amount != 0m)
        {
            rows.Add(new DetailMetric(label, amount, semantic));
        }
    }
}
