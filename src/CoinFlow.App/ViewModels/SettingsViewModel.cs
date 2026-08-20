using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SettingsViewModel(
    CoinFlowService service) : ViewModelBase
{
    private DateOnly _projectionAnchorDate;
    private PaymentAssignmentStrategy? _pendingStrategy;

    public ObservableCollection<StrategyHistoryLine> StrategyHistory { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];
    public ObservableCollection<SelectionOption<DateOnly>> EffectiveSalaryDates { get; } = [];

    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string projectionStartingSavings = "0";
    [ObservableProperty] private string projectionAnchorText = "—";
    [ObservableProperty] private string currentStrategyText = "Henüz seçilmedi";
    [ObservableProperty] private string currentStrategySinceText = string.Empty;
    [ObservableProperty] private string pendingStrategyText = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;
    [ObservableProperty] private bool canManageStrategy;
    [ObservableProperty] private bool hasNoStrategy = true;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedEffectiveSalary;
    [ObservableProperty] private string strategyNote = string.Empty;
    [ObservableProperty] private string previewText = string.Empty;
    [ObservableProperty] private bool hasPreview;

    public bool IsDevelopment => BuildInfo.IsDevelopment;
    public string BuildChannel => BuildInfo.Channel;
    public string VersionText => $"Sürüm {BuildInfo.Version}";
    public string CommitText => $"Commit {BuildInfo.Commit}";
    public string BuildText => $"Build #{BuildInfo.BuildNumber}";

    public async Task LoadAsync()
    {
        var plan = await service.GetFinancialPlanAsync();
        var settings = plan.Settings;
        var overview = await service.GetPaymentAssignmentStrategyOverviewAsync();
        _projectionAnchorDate = settings.ProjectionAnchorDate;
        SalaryDay = settings.SalaryDay.ToString(TurkishCulture);
        MonthlyLivingBudget = settings.MonthlyLivingBudget
            .ToString("N2", TurkishCulture);
        ProjectionStartingSavings = settings.ProjectionStartingSavings
            .ToString("N2", TurkishCulture);
        ProjectionAnchorText = settings.ProjectionAnchorDate == default
            ? "İlk maaş kaydıyla oluşturulacak"
            : settings.ProjectionAnchorDate.ToString(
                "dd MMMM yyyy", TurkishCulture);

        CanManageStrategy = overview.Current is not null;
        HasNoStrategy = !CanManageStrategy;
        CurrentStrategyText = overview.Current is null
            ? "Henüz seçilmedi"
            : ModeText(overview.Current.Mode);
        CurrentStrategySinceText = overview.Current is null
            ? plan.Salaries.Count == 0
                ? "İlk maaşını eklediğinde kullanım düzenini seçersin."
                : "Maaş kullanım düzenini seçerek projeksiyonu tamamla."
            : overview.Current.EffectiveFromSalaryDate >
              DateOnly.FromDateTime(DateTime.Today)
                ? $"{overview.Current.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından itibaren"
                : $"{overview.Current.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından beri";
        _pendingStrategy = overview.Pending;
        HasPendingStrategy = overview.Pending is not null;
        PendingStrategyText = overview.Pending is null
            ? string.Empty
            : $"{overview.Pending.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından itibaren {ModeText(overview.Pending.Mode)}";

        StrategyHistory.Clear();
        foreach (var strategy in overview.History.OrderByDescending(x =>
                     x.EffectiveFromSalaryDate))
        {
            StrategyHistory.Add(new StrategyHistoryLine(
                strategy.Id,
                strategy.EffectiveFromSalaryDate.ToString(
                    "dd MMMM yyyy", TurkishCulture),
                ModeText(strategy.Mode),
                strategy.Note,
                strategy.EffectiveFromSalaryDate > DateOnly.FromDateTime(
                    DateTime.Today)));
        }

        EffectiveSalaryDates.Clear();
        foreach (var date in overview.AvailableEffectiveSalaryDates)
        {
            EffectiveSalaryDates.Add(new SelectionOption<DateOnly>(
                $"{date.ToString("dd MMMM yyyy", TurkishCulture)} maaşı",
                date));
        }

        var defaultMode = overview.Pending?.Mode ??
                          (overview.Current is null
                              ? PaymentAssignmentMode.UpcomingPeriod
                              : Opposite(overview.Current.Mode));
        SelectedStrategyMode = StrategyModes.First(x =>
            x.Value == defaultMode);
        SelectedEffectiveSalary = overview.Pending is null
            ? EffectiveSalaryDates.FirstOrDefault()
            : EffectiveSalaryDates.FirstOrDefault(x =>
                  x.Value == overview.Pending.EffectiveFromSalaryDate) ??
              EffectiveSalaryDates.FirstOrDefault();
        StrategyNote = overview.Pending?.Note ?? "Planlanan düzen değişikliği";
        HasPreview = false;
    }

    public void PrepareStrategyEditor()
    {
        if (!CanManageStrategy)
        {
            SetStatus(
                "Önce maaşını ekleyip ilk maaş kullanım düzenini seçmelisin.");
            return;
        }

        HasPreview = false;
        SetStatus(string.Empty);
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private async Task PreviewStrategyAsync()
    {
        try
        {
            var preview = await service.PreviewPaymentAssignmentStrategyAsync(
                SelectedStrategyMode?.Value ?? throw new InvalidOperationException(
                    "Yeni düzen seçilmelidir."),
                SelectedEffectiveSalary?.Value ?? throw new InvalidOperationException(
                    "Geçerli maaş tarihi seçilmelidir."));
            PreviewText = string.Join(Environment.NewLine,
                $"Geçerli maaş: {preview.EffectiveSalaryDate:dd.MM.yyyy}",
                $"Mevcut: {ModeText(preview.CurrentMode)}",
                $"Yeni: {ModeText(preview.NewMode)}",
                $"Normal zorunlu yük: {Money(preview.Baseline.MandatoryOutflow)}",
                $"Geçmiş düzenden kapanacak: {Money(preview.Scenario.TransitionCatchUpAmount)}",
                $"Yeni dönem için ayrılacak: {Money(preview.Scenario.ForwardFundedAmount)}",
                $"Toplam geçiş yükü: {Money(preview.TotalTransitionBurden)}",
                $"Tahmini tasarruf: {Money(preview.Scenario.EstimatedSavingsCapacity)}",
                $"Tahmini birikim: {Money(preview.Scenario.EndingProjectedSavings)}",
                preview.FinancingGap < 0m
                    ? $"Finansman açığı: {Money(preview.FinancingGap)}"
                    : "Finansman açığı oluşmuyor.");
            HasPreview = true;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            HasPreview = false;
            SetStatus(exception.Message);
        }
    }

    public async Task<bool> ApplyStrategyAsync()
    {
        try
        {
            var date = SelectedEffectiveSalary?.Value ??
                       throw new InvalidOperationException(
                           "Geçerli maaş tarihi seçilmelidir.");
            var mode = SelectedStrategyMode?.Value ??
                       throw new InvalidOperationException(
                           "Yeni düzen seçilmelidir.");
            await service.SavePaymentAssignmentStrategyAsync(
                new PaymentAssignmentStrategy
                {
                    Id = _pendingStrategy?.Id ?? Guid.NewGuid(),
                    Mode = mode,
                    EffectiveFromSalaryDate = date,
                    Note = StrategyNote.Trim()
                });
            await LoadAsync();
            SetStatus("Maaş kullanım düzeni planlandı; geçmiş kayıtlar korundu.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    public async Task<bool> DeletePendingStrategyAsync()
    {
        if (_pendingStrategy is null)
        {
            return false;
        }

        try
        {
            await service.DeletePaymentAssignmentStrategyAsync(
                _pendingStrategy.Id);
            await LoadAsync();
            SetStatus("Planlanan düzen değişikliği iptal edildi.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if (!int.TryParse(SalaryDay, out var day) || day is < 1 or > 31)
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
                ProjectionAnchorDate = _projectionAnchorDate
            });
            SetStatus("Ayarlar kaydedildi.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public async Task<bool> ClearDevelopmentDataAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus("Bu işlem yalnızca development build'de kullanılabilir.");
            return false;
        }

        try
        {
            await service.ClearDevelopmentDataAsync();
            await LoadAsync();
            SetStatus("Tüm veriler silindi.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    public async Task<bool> LoadCanonicalSeedAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus("Bu işlem yalnızca development build'de kullanılabilir.");
            return false;
        }

        try
        {
            await service.LoadCanonicalDevelopmentDataAsync();
            await LoadAsync();
            SetStatus("Canonical development verisi yüklendi.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    private static PaymentAssignmentMode Opposite(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? PaymentAssignmentMode.UpcomingPeriod
            : PaymentAssignmentMode.PreviousPeriod;

    private static string ModeText(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";
}
