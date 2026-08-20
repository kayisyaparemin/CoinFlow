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
    private readonly Dictionary<Guid, PaymentAssignmentStrategy>
        _strategyById = [];

    public ObservableCollection<StrategyHistoryLine> StrategyHistory { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];
    public ObservableCollection<SelectionOption<DateOnly>> EffectiveSalaryDates { get; } = [];
    public ObservableCollection<SelectionOption<Guid>> HistoricalStrategyOptions { get; } = [];

    [ObservableProperty] private string salaryDay = "10";
    [ObservableProperty] private string monthlyLivingBudget = "0";
    [ObservableProperty] private string projectionStartingSavings = "0";
    [ObservableProperty] private string projectionAnchorText = "—";
    [ObservableProperty] private string currentStrategyText = "—";
    [ObservableProperty] private string currentStrategySinceText = "—";
    [ObservableProperty] private string pendingStrategyText = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;
    [ObservableProperty] private bool isStrategyEditorVisible;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedEffectiveSalary;
    [ObservableProperty] private string strategyNote = string.Empty;
    [ObservableProperty] private string previewText = string.Empty;
    [ObservableProperty] private bool hasPreview;
    [ObservableProperty] private bool isHistoricalEditorVisible;
    [ObservableProperty] private SelectionOption<Guid>? selectedHistoricalStrategy;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedHistoricalMode;

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
        ProjectionAnchorText = settings.ProjectionAnchorDate
            .ToString("dd MMMM yyyy", TurkishCulture);
        CurrentStrategyText = ModeText(overview.Current.Mode);
        CurrentStrategySinceText =
            $"{overview.Current.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından beri";
        _pendingStrategy = overview.Pending;
        HasPendingStrategy = overview.Pending is not null;
        PendingStrategyText = overview.Pending is null
            ? string.Empty
            : $"{overview.Pending.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından itibaren {ModeText(overview.Pending.Mode)}";

        StrategyHistory.Clear();
        HistoricalStrategyOptions.Clear();
        _strategyById.Clear();
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
                    DateTime.Now)));
            _strategyById[strategy.Id] = strategy;
            HistoricalStrategyOptions.Add(new SelectionOption<Guid>(
                $"{strategy.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} • {ModeText(strategy.Mode)}",
                strategy.Id));
        }

        EffectiveSalaryDates.Clear();
        foreach (var date in overview.AvailableEffectiveSalaryDates)
        {
            EffectiveSalaryDates.Add(new SelectionOption<DateOnly>(
                $"{date.ToString("dd MMMM yyyy", TurkishCulture)} maaşı",
                date));
        }

        SelectedStrategyMode = StrategyModes.First(x =>
            x.Value == (overview.Pending?.Mode ?? Opposite(overview.Current.Mode)));
        SelectedEffectiveSalary = overview.Pending is null
            ? EffectiveSalaryDates.FirstOrDefault()
            : EffectiveSalaryDates.FirstOrDefault(x =>
                  x.Value == overview.Pending.EffectiveFromSalaryDate) ??
              EffectiveSalaryDates.FirstOrDefault();
        StrategyNote = overview.Pending?.Note ?? "Planlanan düzen değişikliği";
        SelectedHistoricalStrategy = HistoricalStrategyOptions.FirstOrDefault();
        SelectedHistoricalMode = StrategyModes.FirstOrDefault(x =>
            x.Value == overview.Current.Mode);
        HasPreview = false;
    }

    [RelayCommand]
    private void ShowHistoricalEditor()
    {
        IsHistoricalEditorVisible = true;
        if (SelectedHistoricalStrategy is not null &&
            _strategyById.TryGetValue(
                SelectedHistoricalStrategy.Value,
                out var strategy))
        {
            SelectedHistoricalMode = StrategyModes.First(x =>
                x.Value == strategy.Mode);
        }
    }

    partial void OnSelectedHistoricalStrategyChanged(
        SelectionOption<Guid>? value)
    {
        if (value is not null &&
            _strategyById.TryGetValue(value.Value, out var strategy))
        {
            SelectedHistoricalMode = StrategyModes.First(x =>
                x.Value == strategy.Mode);
        }
    }

    public async Task<bool> CorrectHistoricalStrategyAsync()
    {
        try
        {
            var id = SelectedHistoricalStrategy?.Value ??
                     throw new InvalidOperationException(
                         "Düzeltilecek geçmiş kayıt seçilmelidir.");
            var existing = _strategyById[id];
            var mode = SelectedHistoricalMode?.Value ??
                       throw new InvalidOperationException(
                           "Düzeltilmiş düzen seçilmelidir.");
            await service.SavePaymentAssignmentStrategyAsync(
                existing with { Mode = mode },
                confirmedHistoricalCorrection: true);
            await LoadAsync();
            IsHistoricalEditorVisible = false;
            SetStatus("Geçmiş düzen kaydı açık onayla düzeltildi.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    [RelayCommand]
    private void ShowStrategyEditor()
    {
        IsStrategyEditorVisible = true;
        HasPreview = false;
    }

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
                $"Geçişte geçmişten kapanacak: {Money(preview.Scenario.TransitionCatchUpAmount)}",
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
            var id = _pendingStrategy?.Id ?? Guid.NewGuid();
            await service.SavePaymentAssignmentStrategyAsync(
                new PaymentAssignmentStrategy
                {
                    Id = id,
                    Mode = mode,
                    EffectiveFromSalaryDate = date,
                    Note = StrategyNote.Trim()
                });
            await LoadAsync();
            IsStrategyEditorVisible = false;
            SetStatus("Maaş kullanım düzeni planlandı.");
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
            SetStatus("Planlanan düzen değişikliği silindi.");
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

    public async Task<bool> ResetDevelopmentDataAsync()
    {
        if (!IsDevelopment)
        {
            SetStatus("Development veri sıfırlama yalnızca development build'de kullanılabilir.");
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

    private static PaymentAssignmentMode Opposite(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? PaymentAssignmentMode.UpcomingPeriod
            : PaymentAssignmentMode.PreviousPeriod;

    private static string ModeText(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";
}
