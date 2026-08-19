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
                    row.Period.Start.ToString("MMMM yyyy", TurkishCulture),
                    Money(row.Salary),
                    Money(row.TotalObligations),
                    Money(row.Spendable),
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
