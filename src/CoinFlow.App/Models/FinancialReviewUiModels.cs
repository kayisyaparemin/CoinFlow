using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.Models;

public partial class ActualPaymentInputItem : ObservableObject
{
    private static readonly CultureInfo Turkish =
        CultureInfo.GetCultureInfo("tr-TR");

    public Guid PlanLineId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly PlannedDate { get; init; }
    public decimal? PlannedAmountValue { get; init; }
    public string PlannedText => PlannedAmountValue is null
        ? "Henüz belirlenmedi"
        : $"{PlannedAmountValue.Value.ToString("N2", Turkish)} TL";
    public string PlannedDateText =>
        PlannedDate.ToString("dd.MM.yyyy", Turkish);
    public ObservableCollection<SelectionOption<ActualPaymentStatus>>
        StatusOptions
    { get; } =
    [
        new("Ödendi", ActualPaymentStatus.Paid),
        new("Farklı tutar ödendi", ActualPaymentStatus.DifferentAmount),
        new("Ödenmedi", ActualPaymentStatus.Unpaid)
    ];

    [ObservableProperty]
    private SelectionOption<ActualPaymentStatus>? selectedStatus;

    [ObservableProperty] private string actualAmount = string.Empty;
    [ObservableProperty] private DateTime actualDate = DateTime.Today;
    [ObservableProperty] private string note = string.Empty;
}

public sealed record ActualFlowInputItem(
    Guid Id,
    ActualFlowType Type,
    string Name,
    string Category,
    DateOnly Date,
    decimal Amount)
{
    public string TypeText => Type == ActualFlowType.UnplannedIncome
        ? "Plan dışı gelir"
        : "Plan dışı ödeme";
    public string DetailText => $"{Date:dd.MM.yyyy} • {Category}";
    public string AmountText =>
        $"{Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";
}

public sealed record ComparisonUiLine(
    string Category,
    string Planned,
    string Actual,
    string Difference);

public sealed record HistoryCardItem(
    Guid ActualId,
    string Period,
    string Planned,
    string Actual,
    string Difference,
    string Status);

public sealed record PaymentHistoryUiLine(
    string Name,
    string Date,
    string Planned,
    string Actual,
    string Status);
