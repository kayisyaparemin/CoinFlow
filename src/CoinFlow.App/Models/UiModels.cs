using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public partial class ProjectionLine(
    string period,
    string assignment,
    string availableAfterMandatory,
    string carryOverDeficit,
    string availableAfterCarryOverDeficit,
    bool hasCarryOverDeficit,
    string livingBudget,
    string plannedLargeCashExpenses,
    bool hasPlannedLargeCashExpenses,
    string estimatedSavings,
    string endingBeforeDeficitInterest,
    string cardInterest,
    string deficitInterest,
    string totalInterest,
    string interestLabel,
    bool hasInterest,
    string endingProjectedSavings,
    string carryOverMessage,
    string breakdown,
    string notice,
    bool hasNotice) : ObservableObject
{
    public string Period { get; } = period;
    public string Assignment { get; } = assignment;
    public string AvailableAfterMandatory { get; } = availableAfterMandatory;
    public string CarryOverDeficit { get; } = carryOverDeficit;
    public string AvailableAfterCarryOverDeficit { get; } =
        availableAfterCarryOverDeficit;
    public bool HasCarryOverDeficit { get; } = hasCarryOverDeficit;
    public string LivingBudget { get; } = livingBudget;
    public string PlannedLargeCashExpenses { get; } =
        plannedLargeCashExpenses;
    public bool HasPlannedLargeCashExpenses { get; } =
        hasPlannedLargeCashExpenses;
    public string EstimatedSavings { get; } = estimatedSavings;
    public string EndingBeforeDeficitInterest { get; } =
        endingBeforeDeficitInterest;
    public string CardInterest { get; } = cardInterest;
    public string DeficitInterest { get; } = deficitInterest;
    public string TotalInterest { get; } = totalInterest;
    public string InterestLabel { get; } = interestLabel;
    public bool HasInterest { get; } = hasInterest;
    public string EndingProjectedSavings { get; } = endingProjectedSavings;
    public string CarryOverMessage { get; } = carryOverMessage;
    public string Breakdown { get; } = breakdown;
    public string Notice { get; } = notice;
    public bool HasNotice { get; } = hasNotice;

    [ObservableProperty] private bool isExpanded;
    public bool IsCollapsed => !IsExpanded;

    partial void OnIsExpandedChanged(bool value) =>
        OnPropertyChanged(nameof(IsCollapsed));

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed record SimulationLine(
    string Period,
    string BaselineSavings,
    string ScenarioSavings,
    string Difference,
    string AvailableAfterMandatory,
    string CarryOverDeficit,
    string AvailableAfterCarryOverDeficit,
    bool HasCarryOverDeficit,
    string SavingsCapacity,
    string CardInterest,
    string DeficitInterest,
    string TotalInterest,
    bool HasInterest);
