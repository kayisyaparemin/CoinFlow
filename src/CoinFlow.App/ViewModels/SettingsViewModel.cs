using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SettingsViewModel(
    CoinFlowService service) : ViewModelBase
{
    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string projectionStartingSavings = "0";
    [ObservableProperty] private bool isPreviousPeriod;
    [ObservableProperty] private bool isUpcomingPeriod = true;
    [ObservableProperty] private string previousPeriodDescription = string.Empty;
    [ObservableProperty] private string upcomingPeriodDescription = string.Empty;
    private PaymentAssignmentMode _paymentAssignmentMode =
        PaymentAssignmentMode.UpcomingPeriod;
    private bool _isChangingAssignmentMode;

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
        SetAssignmentMode(settings.PaymentAssignmentMode);
        UpdateAssignmentDescriptions();
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
                    "Projeksiyon başlangıç birikimi"),
                PaymentAssignmentMode = _paymentAssignmentMode
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

    partial void OnSalaryDayChanged(string value) =>
        UpdateAssignmentDescriptions();

    partial void OnIsPreviousPeriodChanged(bool value)
    {
        if (!_isChangingAssignmentMode && value)
        {
            SetAssignmentMode(PaymentAssignmentMode.PreviousPeriod);
        }
    }

    partial void OnIsUpcomingPeriodChanged(bool value)
    {
        if (!_isChangingAssignmentMode && value)
        {
            SetAssignmentMode(PaymentAssignmentMode.UpcomingPeriod);
        }
    }

    private void SetAssignmentMode(PaymentAssignmentMode mode)
    {
        _paymentAssignmentMode = mode;
        _isChangingAssignmentMode = true;
        IsPreviousPeriod = mode == PaymentAssignmentMode.PreviousPeriod;
        IsUpcomingPeriod = mode == PaymentAssignmentMode.UpcomingPeriod;
        _isChangingAssignmentMode = false;
    }

    private void UpdateAssignmentDescriptions()
    {
        var day = int.TryParse(SalaryDay, out var parsed) &&
                  parsed is >= 1 and <= 31
            ? parsed
            : 10;
        var octoberSalary = CalendarRules.ResolveDay(2026, 10, day);
        var previousSalary = CalendarRules.ResolveDay(2026, 9, day);
        var nextSalary = CalendarRules.ResolveDay(2026, 11, day);
        PreviousPeriodDescription =
            $"{DateText(octoberSalary)} maaşı, " +
            $"{DateText(previousSalary.AddDays(1))}–{DateText(octoberSalary)} arasındaki ödemeleri kapatır.";
        UpcomingPeriodDescription =
            $"{DateText(octoberSalary)} maaşı, " +
            $"{DateText(octoberSalary)}–{DateText(nextSalary.AddDays(-1))} arasındaki ödemeleri karşılar.";
    }

    private static string DateText(DateOnly date) =>
        date.ToString("d MMMM", TurkishCulture);
}
