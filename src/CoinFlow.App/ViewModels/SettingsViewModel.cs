using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SettingsViewModel(CoinFlowService service) : ViewModelBase
{
    private Guid _fundId;

    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private bool gamificationEnabled = true;
    [ObservableProperty] private string bufferTarget = string.Empty;
    [ObservableProperty] private string bufferCurrent = string.Empty;
    [ObservableProperty] private string periodContribution = "0";
    [ObservableProperty] private string transferAmount = string.Empty;

    public bool IsDevelopment => BuildInfo.IsDevelopment;
    public string BuildChannel => BuildInfo.Channel;
    public string VersionText => $"Sürüm {BuildInfo.Version}";
    public string CommitText => $"Commit {BuildInfo.Commit}";
    public string BuildText => $"Build #{BuildInfo.BuildNumber}";

    public async Task LoadAsync()
    {
        var data = await service.GetFinanceDataAsync();
        _fundId = data.EmergencyFund.Id;
        SalaryDay = data.Settings.SalaryDay.ToString(TurkishCulture);
        GamificationEnabled = data.Settings.GamificationEnabled;
        BufferTarget = data.EmergencyFund.TargetAmount.ToString("0.##", TurkishCulture);
        BufferCurrent = data.EmergencyFund.CurrentAmount.ToString("0.##", TurkishCulture);
        PeriodContribution = data.EmergencyFund.PlannedPeriodContribution.ToString("0.##", TurkishCulture);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            if (!int.TryParse(SalaryDay, out var day) || day is < 1 or > 31)
            {
                throw new InvalidOperationException("Maaş günü 1 ile 31 arasında olmalıdır.");
            }

            await service.SaveSettingsAsync(new UserSettings
            {
                SalaryDay = day,
                GamificationEnabled = GamificationEnabled,
                DevelopmentSeedEnabled = BuildInfo.IsDevelopment
            });
            await service.SaveEmergencyFundAsync(new EmergencyFund
            {
                Id = _fundId,
                TargetAmount = ParseMoney(BufferTarget, "Tampon hedefi"),
                CurrentAmount = ParseMoney(BufferCurrent, "Mevcut tampon"),
                PlannedPeriodContribution = ParseMoney(PeriodContribution, "Dönem katkısı")
            });
            SetStatus("Ayarlar kaydedildi.");
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

    [RelayCommand]
    private async Task TransferAsync()
    {
        try
        {
            var amount = ParseMoney(TransferAmount, "Aktarım tutarı");
            await service.TransferToEmergencyFundAsync(amount);
            TransferAmount = string.Empty;
            SetStatus("Tutar acil durum tamponuna aktarıldı.");
            await LoadAsync();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public async Task<bool> ResetAllDataAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await service.ResetAllDataAsync();
            TransferAmount = string.Empty;
            await LoadAsync();
            SetStatus("Tüm veriler sıfırlandı.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
