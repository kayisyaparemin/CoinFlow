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
    decimal Amount)
{
    public string DateText => Date.ToString("dd.MM.yyyy");
    public string AmountText =>
        $"{Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";
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

public partial class ProjectionLine(
    string period,
    string assignment,
    string availableAfterMandatory,
    string estimatedSavings,
    string endingProjectedSavings,
    string breakdown,
    string notice,
    bool hasNotice) : ObservableObject
{
    public string Period { get; } = period;
    public string Assignment { get; } = assignment;
    public string AvailableAfterMandatory { get; } = availableAfterMandatory;
    public string EstimatedSavings { get; } = estimatedSavings;
    public string EndingProjectedSavings { get; } = endingProjectedSavings;
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
    string SavingsCapacity);
