using System.Globalization;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public enum DetailSemanticType
{
    Neutral,
    Income,
    Mandatory,
    Savings,
    Deficit,
    Interest,
    Expense,
    Projection
}

public sealed record DetailMetric(
    string Label,
    decimal Amount,
    DetailSemanticType Semantic,
    bool ShowPositiveSign = false,
    bool IsTotal = false,
    int DecimalPlaces = 2,
    string? DisplayText = null)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public string AmountText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayText))
            {
                return DisplayText;
            }

            var formatted = Math.Abs(Amount).ToString(
                DecimalPlaces == 0 ? "N0" : "N2",
                TurkishCulture);
            var sign = Amount < 0m
                ? "-"
                : ShowPositiveSign && Amount > 0m
                    ? "+"
                    : string.Empty;
            return $"{sign}{formatted} TL";
        }
    }

    public bool IsIncome => Semantic == DetailSemanticType.Income;
    public bool IsMandatory => Semantic == DetailSemanticType.Mandatory;
    public bool IsSavings => Semantic == DetailSemanticType.Savings;
    public bool IsDeficit => Semantic == DetailSemanticType.Deficit;
    public bool IsInterest => Semantic == DetailSemanticType.Interest;
    public bool IsExpense => Semantic == DetailSemanticType.Expense;
    public bool IsProjection => Semantic == DetailSemanticType.Projection;
}

public sealed record DetailPaymentRow(
    DateOnly Date,
    string Name,
    string Category,
    decimal? Amount,
    DetailSemanticType Semantic,
    DateOnly AssignedSalaryDate,
    bool IsEstimated,
    bool IsBeforeFundingSalary,
    bool IsUndetermined,
    string Detail = "")
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public string DateText => Date.ToString("dd MMM", TurkishCulture);
    public string AmountText => Amount is decimal value
        ? $"{value.ToString("N2", TurkishCulture)} TL"
        : "Ödeme belirlenmedi";
    public string AssignedSalaryText => AssignedSalaryDate == default
        ? string.Empty
        : $"Karşılayan maaş: {AssignedSalaryDate.ToString("dd MMMM", TurkishCulture)}";
    public bool HasAssignedSalary => AssignedSalaryDate != default;
    public bool IsExpense => Semantic == DetailSemanticType.Expense;
}

public sealed record DetailComparisonRow(
    string Label,
    decimal BaselineAmount,
    decimal ScenarioAmount,
    decimal Difference,
    bool HigherIsBetter)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public string BaselineText => Money(BaselineAmount);
    public string ScenarioText => Money(ScenarioAmount);
    public string DifferenceText => SignedMoney(Difference);
    public bool IsFavorable => Difference != 0m &&
        (HigherIsBetter ? Difference > 0m : Difference < 0m);
    public bool IsUnfavorable => Difference != 0m && !IsFavorable;
    public bool IsNeutral => Difference == 0m;

    private static string Money(decimal value) =>
        $"{value.ToString("N2", TurkishCulture)} TL";

    private static string SignedMoney(decimal value)
    {
        var sign = value > 0m ? "+" : string.Empty;
        return $"{sign}{value.ToString("N2", TurkishCulture)} TL";
    }
}

public sealed record DetailDeficitCallout(
    decimal OpeningDeficit,
    decimal CoveredThisPeriod,
    decimal RemainingDeficit,
    bool IsRecovered)
{
    private static readonly CultureInfo TurkishCulture =
        CultureInfo.GetCultureInfo("tr-TR");

    public string OpeningDeficitText =>
        $"{OpeningDeficit.ToString("N2", TurkishCulture)} TL";
    public string CoveredText =>
        $"Bu dönem karşılanan: {CoveredThisPeriod.ToString("N2", TurkishCulture)} TL";
    public bool HasCoveredAmount => CoveredThisPeriod > 0m;
    public string Message => IsRecovered
        ? "Bu dönemde açık tamamen kapanıyor."
        : $"Sonraki döneme {RemainingDeficit.ToString("N2", TurkishCulture)} TL açık devrediyor.";
}

public sealed record SalaryPeriodDetailData(
    DateOnly SalaryDate,
    string SalaryTitle,
    string PaymentWindowText,
    string AssignmentBadge,
    bool IsStrategyTransition,
    DetailMetric IncomeSummary,
    DetailMetric MandatorySummary,
    DetailMetric SavingsSummary,
    DetailMetric EndingSummary,
    IReadOnlyList<DetailMetric> FlowRows,
    DetailDeficitCallout? Deficit,
    IReadOnlyList<DetailMetric> MandatoryRows,
    IReadOnlyList<DetailMetric> InterestRows,
    IReadOnlyList<DetailMetric> CardInterestRows,
    IReadOnlyList<DetailMetric> TransitionRows,
    IReadOnlyList<DetailPaymentRow> PaymentRows,
    IReadOnlyList<DetailComparisonRow> ComparisonRows,
    IReadOnlyList<DetailMetric> DebugRows)
{
    public bool HasDeficit => Deficit is not null;
    public bool HasMandatoryRows => MandatoryRows.Count > 0;
    public bool HasInterestRows => InterestRows.Count > 0;
    public bool HasCardInterestRows => CardInterestRows.Count > 0;
    public bool HasTransitionRows => TransitionRows.Count > 0;
    public bool HasPayments => PaymentRows.Count > 0;
    public bool HasComparison => ComparisonRows.Count > 0;
    public bool HasBeforeSalaryPayments =>
        PaymentRows.Any(x => x.IsBeforeFundingSalary);
    public bool HasEstimatedPayments =>
        PaymentRows.Any(x => x.IsEstimated);
}

public sealed record SalaryPeriodDetailRequest(
    SalaryPeriodProjection Scenario,
    SalaryPeriodProjection? Baseline = null);
