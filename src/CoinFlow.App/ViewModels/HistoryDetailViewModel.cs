using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class HistoryDetailViewModel(
    CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<ComparisonUiLine> ComparisonLines { get; } = [];
    public ObservableCollection<PaymentHistoryUiLine> Payments { get; } = [];

    [ObservableProperty] private string periodText = string.Empty;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string originalLiving = string.Empty;
    [ObservableProperty] private string finalLiving = string.Empty;
    [ObservableProperty] private string actualLiving = string.Empty;
    [ObservableProperty] private bool hasRevision;
    [ObservableProperty] private string revisionNotice = string.Empty;
    [ObservableProperty] private string plannedEnding = string.Empty;
    [ObservableProperty] private string actualEnding = string.Empty;
    [ObservableProperty] private string difference = string.Empty;
    [ObservableProperty] private string newSnapshotText = string.Empty;
    [ObservableProperty] private string reconciliationText = string.Empty;

    public async Task LoadAsync(Guid actualId)
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var period = await service.GetHistoryPeriodAsync(actualId);
            var plan = period.OriginalPlan;
            PeriodText =
                $"{plan.PeriodStart:dd MMMM yyyy} → {plan.PeriodEnd:dd MMMM yyyy}";
            Summary = period.Comparison.Summary;
            OriginalLiving = Money(plan.PlannedLivingBudget, 2);
            HasRevision = period.Revision is not null;
            FinalLiving = Money(
                period.Revision?.PlannedLivingBudget ??
                plan.PlannedLivingBudget,
                2);
            ActualLiving = Money(period.Actual.ActualLivingSpend, 2);
            RevisionNotice = HasRevision
                ? $"Plan dönem içinde {period.RevisionCount} kez güncellendi; ilk plan korunuyor."
                : string.Empty;
            PlannedEnding = Money(
                period.Comparison.PlannedEndingSavings,
                2);
            ActualEnding = Money(
                period.Comparison.ActualEndingSavings,
                2);
            Difference = SignedMoney(period.Comparison.Difference);
            NewSnapshotText =
                $"{period.ResultSnapshot.SnapshotDate:dd MMMM yyyy} • {Money(period.ResultSnapshot.ProjectionStartingSavings, 2)}";
            ReconciliationText = SignedMoney(
                period.Actual.ReconciliationAdjustment);

            ComparisonLines.Clear();
            foreach (var line in period.Comparison.Lines)
            {
                ComparisonLines.Add(new ComparisonUiLine(
                    line.Category,
                    Money(line.Planned, 2),
                    Money(line.Actual, 2),
                    SignedMoney(line.Difference)));
            }

            Payments.Clear();
            foreach (var payment in period.Actual.Payments)
            {
                Payments.Add(new PaymentHistoryUiLine(
                    payment.Name,
                    payment.PlannedDate.ToString("dd.MM.yyyy"),
                    payment.PlannedAmount is null
                        ? "Belirsizdi"
                        : Money(payment.PlannedAmount.Value, 2),
                    Money(payment.ActualAmount, 2),
                    payment.Status switch
                    {
                        ActualPaymentStatus.Paid => "Ödendi",
                        ActualPaymentStatus.DifferentAmount =>
                            "Farklı tutar",
                        _ => "Ödenmedi"
                    }));
            }
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SignedMoney(decimal value) =>
        $"{(value > 0m ? "+" : string.Empty)}{value.ToString("N2", TurkishCulture)} TL";
}
