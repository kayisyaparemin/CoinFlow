using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SettingsViewModel(
    CoinFlowService service) : ViewModelBase
{
    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string projectionStartingSavings = "0";

    public bool IsDevelopment => BuildInfo.IsDevelopment;
    public string BuildChannel => BuildInfo.Channel;
    public string VersionText => $"Sürüm {BuildInfo.Version}";
    public string CommitText => $"Commit {BuildInfo.Commit}";
    public string BuildText => $"Build #{BuildInfo.BuildNumber}";

    public async Task LoadAsync()
    {
        var settings = (await service.GetFinancialPlanAsync()).Settings;
        SalaryDay = settings.SalaryDay.ToString(TurkishCulture);
        MonthlyLivingBudget = settings.MonthlyLivingBudget
            .ToString("N2", TurkishCulture);
        ProjectionStartingSavings = settings.ProjectionStartingSavings
            .ToString("N2", TurkishCulture);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if (!int.TryParse(SalaryDay, out var day) ||
                day is < 1 or > 31)
            {
                throw new InvalidOperationException(
                    "Maaş günü 1 ile 31 arasında olmalıdır.");
            }

            await service.SaveSettingsAsync(new UserSettings
            {
                SalaryDay = day,
                MonthlyLivingBudget = ParseMoney(
                    MonthlyLivingBudget,
                    "Aylık tahmini yaşam bütçesi"),
                ProjectionStartingSavings = ParseMoney(
                    ProjectionStartingSavings,
                    "Projeksiyon başlangıç birikimi")
            });
            SetStatus("Ayarlar kaydedildi.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public async Task<bool> ResetDevelopmentDataAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus(
                "Development veri sıfırlama yalnızca development build'de kullanılabilir.");
            return false;
        }

        try
        {
            await service.ResetDevelopmentDataAsync();
            await LoadAsync();
            SetStatus("Canonical development verisi yeniden yüklendi.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }
}
