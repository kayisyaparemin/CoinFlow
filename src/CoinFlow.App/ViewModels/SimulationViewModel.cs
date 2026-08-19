using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel(CoinFlowService service) : ViewModelBase
{
    [ObservableProperty] private string name = "Beyaz eşya";
    [ObservableProperty] private string totalAmount = "120000";
    [ObservableProperty] private string installmentCount = "9";
    [ObservableProperty] private DateTime firstPaymentDate = new(2026, 12, 1);

    public ObservableCollection<SimulationLine> Results { get; } = [];

    [RelayCommand]
    private async Task CalculateAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            if (!int.TryParse(InstallmentCount, out var count))
            {
                throw new InvalidOperationException("Taksit sayısı geçerli bir tam sayı olmalıdır.");
            }

            var rows = await service.SimulatePurchaseAsync(new PurchaseSimulationRequest(
                Name.Trim(), ParseMoney(TotalAmount, "Toplam tutar"), count, DateOnly.FromDateTime(FirstPaymentDate)));
            Results.Clear();
            foreach (var row in rows)
            {
                Results.Add(new SimulationLine(
                    row.Month.ToString("MMMM yyyy", TurkishCulture),
                    Money(row.BaselineSpendable),
                    Money(row.NewInstallment, 2),
                    Money(row.ResultingSpendable, 2)));
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
}
