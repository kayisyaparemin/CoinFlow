using System.Globalization;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.Models;

public sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public enum ManagementSection
{
    Income,
    Payment
}

public enum FinancialRecordKind
{
    Salary,
    OtherIncome,
    Loan,
    CreditCard,
    TemporaryPlan,
    InstallmentPlan,
    LargeExpense
}

public sealed record FinancialRecordLine(
    Guid Id,
    ManagementSection Section,
    FinancialRecordKind Kind,
    string Title,
    string Subtitle,
    string Amount,
    string Badge = "")
{
    public bool CanEditCard => Kind == FinancialRecordKind.CreditCard;
}

public sealed record DatedAmountLine(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    string Description = "")
{
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string AmountText =>
        $"{Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public sealed record CardPaymentPlanLine(
    Guid Id,
    DateOnly DueDate,
    CreditCardPaymentType PaymentType,
    decimal? Amount)
{
    public string DateText => DueDate.ToString("dd.MM.yyyy");

    public string PaymentText => PaymentType switch
    {
        CreditCardPaymentType.Minimum => "Asgari",
        CreditCardPaymentType.FullStatement => "Ekstre tamamı",
        CreditCardPaymentType.FixedAmount =>
            $"{Amount.GetValueOrDefault().ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL",
        _ => "—"
    };
}

public sealed record UpcomingPaymentLine(
    string Date,
    string Name,
    string Amount,
    string Detail);

public sealed record StrategyHistoryLine(
    Guid Id,
    string EffectiveDate,
    string Mode,
    string Note,
    bool IsFuture);

public sealed class ProjectionLine(
    SalaryPeriodProjection projection,
    string period,
    string assignment,
    string availableAfterMandatory,
    string carryOverDeficit,
    bool hasCarryOverDeficit,
    string estimatedSavings,
    string totalInterest,
    bool hasInterest,
    string endingProjectedSavings,
    string beforeSalaryWarning,
    bool hasBeforeSalaryWarning,
    bool hasEstimatedPayment,
    bool hasUndeterminedPayment)
{
    public SalaryPeriodProjection Projection { get; } = projection;
    public string Period { get; } = period;
    public string Assignment { get; } = assignment;
    public string AvailableAfterMandatory { get; } = availableAfterMandatory;
    public string CarryOverDeficit { get; } = carryOverDeficit;
    public bool HasCarryOverDeficit { get; } = hasCarryOverDeficit;
    public string EstimatedSavings { get; } = estimatedSavings;
    public string TotalInterest { get; } = totalInterest;
    public bool HasInterest { get; } = hasInterest;
    public string EndingProjectedSavings { get; } = endingProjectedSavings;
    public string BeforeSalaryWarning { get; } = beforeSalaryWarning;
    public bool HasBeforeSalaryWarning { get; } = hasBeforeSalaryWarning;
    public bool HasEstimatedPayment { get; } = hasEstimatedPayment;
    public bool HasUndeterminedPayment { get; } = hasUndeterminedPayment;
}

public sealed record SimulationLine(
    SimulationImpactRow Impact,
    string Period,
    string Assignment,
    string BaselineSavings,
    string ScenarioSavings,
    string Difference,
    bool DifferenceIsNegative,
    string InterestDifference,
    bool HasInterestDifference);
