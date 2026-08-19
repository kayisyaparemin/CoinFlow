using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(CoinFlowService service) : ViewModelBase
{
    [ObservableProperty] private string periodText = "—";
    [ObservableProperty] private string salary = "—";
    [ObservableProperty] private string obligations = "—";
    [ObservableProperty] private string periodBudget = "—";
    [ObservableProperty] private string spent = "—";
    [ObservableProperty] private string remaining = "—";
    [ObservableProperty] private string daysRemaining = "—";
    [ObservableProperty] private string dailyCoin = "—";
    [ObservableProperty] private string coinPool = "—";
    [ObservableProperty] private string encouragement = string.Empty;
    [ObservableProperty] private string emergencyFund = "—";
    [ObservableProperty] private double progress;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var snapshot = await service.GetDashboardAsync();
            var start = snapshot.SalaryPeriod.Period.Start;
            var end = snapshot.SalaryPeriod.Period.End;
            PeriodText = $"{start:dd MMM} → {end:dd MMM}".ToUpper(TurkishCulture);
            Salary = Money(snapshot.SalaryPeriod.Salary);
            Obligations = Money(snapshot.SalaryPeriod.TotalObligations);
            PeriodBudget = Money(snapshot.SalaryPeriod.SpendableBudget);
            Spent = Money(snapshot.DailyCoin.PeriodCashSpending);
            Remaining = Money(snapshot.DailyCoin.RemainingBudget);
            DaysRemaining = $"{snapshot.DailyCoin.RemainingDays} gün";
            DailyCoin = Money(snapshot.DailyCoin.SustainableDailyBudget, 2);
            CoinPool = Money(snapshot.DailyCoin.CoinPool, 2);
            Encouragement = snapshot.Encouragement;
            EmergencyFund = $"{Money(snapshot.EmergencyFund.CurrentAmount)} / {Money(snapshot.EmergencyFund.TargetAmount)}";
            Progress = Math.Clamp((double)snapshot.DailyCoin.ProgressRate, 0d, 1d);
        }
        catch (Exception exception)
        {
            SetStatus($"Özet yüklenemedi: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
