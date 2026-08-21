using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public partial class HistoryViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<HistoryCardItem> Periods { get; } = [];

    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool hasSummary;
    [ObservableProperty] private string summaryTitle = string.Empty;
    [ObservableProperty] private string summaryPlanned = string.Empty;
    [ObservableProperty] private string summaryActual = string.Empty;
    [ObservableProperty] private string summaryDifference = string.Empty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var periods = await service.GetHistoryPeriodsAsync();
            Periods.Clear();
            foreach (var period in periods)
            {
                var difference = period.Comparison.Difference;
                Periods.Add(new HistoryCardItem(
                    period.Actual.Id,
                    period.OriginalPlan.PeriodStart.ToString(
                        "MMMM yyyy",
                        TurkishCulture),
                    Money(period.Comparison.PlannedEndingSavings, 2),
                    Money(period.Comparison.ActualEndingSavings, 2),
                    SignedMoney(difference),
                    difference >= 0m
                        ? "Planın üzerinde"
                        : "Planın altında"));
            }

            HasHistory = Periods.Count > 0;
            var summary = await service.GetHistorySummaryAsync();
            HasSummary = summary is not null;
            if (summary is not null)
            {
                SummaryTitle = $"Son {summary.PeriodCount} dönem";
                SummaryPlanned = Money(summary.Planned, 2);
                SummaryActual = Money(summary.Actual, 2);
                SummaryDifference = SignedMoney(summary.Difference);
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
