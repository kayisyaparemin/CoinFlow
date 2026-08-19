using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public partial class FutureMonthsViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<ProjectionLine> Months { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var rows = await service.GetFutureMonthsAsync();
            Months.Clear();
            foreach (var row in rows)
            {
                Months.Add(new ProjectionLine(
                    $"{row.Period.Start:dd MMM yyyy} → {row.Period.End:dd MMM yyyy}".ToUpper(TurkishCulture),
                    Money(row.Salary),
                    Money(row.TotalObligations, 2),
                    Money(row.ProjectedSpendable, 2),
                    row.ActualRemaining is null ? string.Empty : Money(row.ActualRemaining.Value, 2),
                    row.ActualRemaining is not null,
                    Money(row.ProjectedDailyCoin, 2),
                    $"Kredi {Money(row.LoanPayments)} • Kart {Money(row.CardPayments)} • Geçici {Money(row.TemporaryPayments)} • Planlı {Money(row.PlannedInstallments)}",
                    string.Join(" ", row.Highlights)));
            }
        }
        catch (Exception exception)
        {
            SetStatus($"Projeksiyon yüklenemedi: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
