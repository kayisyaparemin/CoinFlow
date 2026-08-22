using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel(
    CoinFlowService service,
    SimulatorInsightService simulatorInsightService) : ViewModelBase
{
    public ObservableCollection<SelectionOption<SimulationScenarioType>>
        ScenarioTypes { get; } =
    [
        new("Nakit satın alma", SimulationScenarioType.CashPurchase),
        new("Karttan tek çekim", SimulationScenarioType.CreditCardSinglePayment),
        new("Kredi kartı taksitli", SimulationScenarioType.CreditCardInstallmentPurchase),
        new("Finansman / kredi", SimulationScenarioType.FinancingLoan),
        new("Nakit borç", SimulationScenarioType.CashDebt),
        new("Tek seferlik ödeme", SimulationScenarioType.FutureOneTimePayment),
        new("Düzenli ödeme", SimulationScenarioType.RecurringPayment),
        new("Tek seferlik gelir", SimulationScenarioType.FutureIncome),
        new("Maaş değişikliği", SimulationScenarioType.SalaryChange),
        new("Maaş kullanım düzeni değişikliği", SimulationScenarioType.PaymentStrategyChange),
        new("Kart ekstresini tamamen kapat", SimulationScenarioType.CreditCardFullPayment)
    ];

    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];
    public ObservableCollection<SimulationDraftConditionView>
        DraftConditions { get; } = [];
    public ObservableCollection<SimulatorPeriodView> Results { get; } = [];
    public ObservableCollection<string> NarrativeInsights { get; } = [];
    public ObservableCollection<SimulatorSummaryMetric> SummaryMetrics { get; } = [];
    public ObservableCollection<SelectionOption<DateOnly>> StrategySalaryDates { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];

    private IReadOnlyList<SimulationRequest> _lastRequests = [];
    private Guid? _editingConditionId;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private bool _preserveOnNextAppearance;

    [ObservableProperty] private string name = "Beyaz eşya";
    [ObservableProperty] private string amount = "120000";
    [ObservableProperty] private SelectionOption<SimulationScenarioType>? selectedScenarioType;
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string paymentCount = "9";
    [ObservableProperty] private DateTime firstPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string totalRepaymentAmount = "145000";
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool needsPaymentCount;
    [ObservableProperty] private bool needsFirstPayment;
    [ObservableProperty] private bool isFinancing;
    [ObservableProperty] private bool isStrategyChange;
    [ObservableProperty] private bool isCardPayoff;
    [ObservableProperty] private bool needsAmount = true;
    [ObservableProperty] private string startDateLabel =
        "Başlangıç / işlem tarihi";
    [ObservableProperty] private bool isRegularScenario = true;
    [ObservableProperty] private SelectionOption<PaymentAssignmentMode>? selectedStrategyMode;
    [ObservableProperty] private SelectionOption<DateOnly>? selectedStrategySalaryDate;
    [ObservableProperty] private string scenarioDescription = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    [NotifyPropertyChangedFor(nameof(HasCurrentResults))]
    [NotifyPropertyChangedFor(nameof(HasStaleResult))]
    private bool hasResults;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    [NotifyPropertyChangedFor(nameof(HasCurrentResults))]
    [NotifyPropertyChangedFor(nameof(HasStaleResult))]
    [NotifyPropertyChangedFor(nameof(RunSimulationButtonText))]
    private bool isResultStale;
    [ObservableProperty] private string baselineEnding = "—";
    [ObservableProperty] private string scenarioEnding = "—";
    [ObservableProperty] private string endingDifference = "—";
    [ObservableProperty] private string tightestPeriod = "—";
    [ObservableProperty] private string lowestAvailable = "—";
    [ObservableProperty] private string lowestSavingsCapacity = "—";
    [ObservableProperty] private string lowestProjectedSavings = "—";
    [ObservableProperty] private string firstNegativePeriod = "Yok";
    [ObservableProperty] private string maximumCarryOverDeficit = "—";
    [ObservableProperty] private string recoveryPeriod = "—";
    [ObservableProperty] private string totalScenarioCost = "—";
    [ObservableProperty] private string monthlyBurden = string.Empty;
    [ObservableProperty] private bool hasMonthlyBurden;
    [ObservableProperty] private string financingCost = string.Empty;
    [ObservableProperty] private bool hasFinancingCost;
    [ObservableProperty] private string baselineInterest = "—";
    [ObservableProperty] private string scenarioInterest = "—";
    [ObservableProperty] private string interestDifference = "—";
    [ObservableProperty] private string interestDifferenceTitle =
        "Ek Faiz Yükü";
    [ObservableProperty] private string friendlySummary = string.Empty;
    [ObservableProperty] private string assignmentModeText = string.Empty;
    [ObservableProperty] private bool hasStrategyTransitionSummary;
    [ObservableProperty] private string strategyTransitionSummary = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunSimulation))]
    [NotifyPropertyChangedFor(nameof(HasNoDraftConditions))]
    private bool isPlanAvailable;
    [ObservableProperty] private bool isPlanUnavailable = true;
    [ObservableProperty] private string emptyStateMessage =
        "Simülasyon yapabilmek için önce temel finans planını oluştur.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isApplyingPlan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyPlan))]
    private bool isPlanApplied;
    [ObservableProperty] private string applyButtonText = "Planı Uygula";
    [ObservableProperty] private string applyConfirmationText =
        "Bu plan gerçek finans planına eklenecek.";

    public bool HasDraftConditions => DraftConditions.Count > 0;
    public bool HasNoDraftConditions => IsPlanAvailable && !HasDraftConditions;
    public bool HasMultipleDraftConditions => DraftConditions.Count > 1;
    public bool CanRunSimulation => IsPlanAvailable && HasDraftConditions;
    public bool IsEditingCondition => _editingConditionId is not null;
    public string DraftConditionCountText =>
        $"{DraftConditions.Count} koşul";
    public string AddConditionButtonText =>
        IsEditingCondition ? "Düzenlemeyi Kaydet" : "Koşul Ekle";
    public string RunSimulationButtonText =>
        IsResultStale ? "Simülasyonu Güncelle" : "Simülasyonu Yap";
    public bool HasCurrentResults => HasResults && !IsResultStale;
    public bool HasStaleResult => HasResults && IsResultStale;
    public bool CanApplyPlan =>
        HasCurrentResults && !IsApplyingPlan && !IsPlanApplied;

    public SimulationApplyResult? LastApplyResult { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            SetStatus(string.Empty);
            var plan = await service.GetFinancialPlanAsync();
            IsPlanAvailable = plan.Salaries.Count > 0 &&
                              plan.PaymentAssignmentStrategies.Count > 0 &&
                              plan.Settings.ProjectionAnchorDate != default;
            IsPlanUnavailable = !IsPlanAvailable;
            if (HasResults)
            {
                MarkResultsStale();
            }
            else
            {
                ResetApplyState(clearRequest: true);
            }
            if (!IsPlanAvailable)
            {
                EmptyStateMessage = plan.Salaries.Count == 0
                    ? "Simülasyon yapabilmek için önce temel finans planını oluştur."
                    : "Simülasyon için maaş kullanım düzenini seçerek finans planını tamamla.";
                AssignmentModeText = string.Empty;
                CreditCards.Clear();
                StrategySalaryDates.Clear();
                DraftConditions.Clear();
                NotifyDraftChanged();
                Results.Clear();
                return;
            }

            var overview = await service.GetPaymentAssignmentStrategyOverviewAsync();
            CreditCards.Clear();
            foreach (var card in plan.CreditCards)
            {
                CreditCards.Add(new SelectionOption<Guid>(
                    $"{card.Bank} {card.Name}".Trim(),
                    card.Id));
            }

            var currentMode = overview.Current?.Mode ??
                              throw new InvalidOperationException(
                                  "Maaş kullanım düzeni bulunamadı.");
            AssignmentModeText = AssignmentModeLabel(currentMode);
            StrategySalaryDates.Clear();
            foreach (var date in overview.AvailableEffectiveSalaryDates)
            {
                StrategySalaryDates.Add(new SelectionOption<DateOnly>(
                    $"{date.ToString("dd MMMM yyyy", TurkishCulture)} maaşı",
                    date));
            }
            SelectedStrategySalaryDate ??= StrategySalaryDates.FirstOrDefault();
            SelectedStrategyMode ??= StrategyModes.First(x =>
                x.Value != currentMode);

            SelectedScenarioType ??= ScenarioTypes[0];
            SelectedCreditCard ??= CreditCards.FirstOrDefault();
        }
        catch (Exception exception)
        {
            IsPlanAvailable = false;
            IsPlanUnavailable = true;
            HasResults = false;
            IsResultStale = false;
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private async Task OpenPeriodDetailAsync(SimulatorPeriodView? line)
    {
        if (line is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            AppShell.PeriodDetailRoute,
            new ShellNavigationQueryParameters
            {
                [SalaryPeriodDetailViewModel.DetailQueryKey] =
                    new SalaryPeriodDetailRequest(
                        line.Projection,
                        IsSimulationScenario: true)
            });
        _preserveOnNextAppearance = true;
    }

    public bool ConsumeDetailReturn()
    {
        if (!_preserveOnNextAppearance)
        {
            return false;
        }

        _preserveOnNextAppearance = false;
        return true;
    }

    partial void OnSelectedScenarioTypeChanged(
        SelectionOption<SimulationScenarioType>? value)
    {
        var type = value?.Value ?? SimulationScenarioType.CashPurchase;
        IsCard = type is
            SimulationScenarioType.CreditCardSinglePayment or
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.CreditCardFullPayment;
        NeedsPaymentCount = type is
            SimulationScenarioType.CreditCardInstallmentPurchase or
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        NeedsFirstPayment = type is
            SimulationScenarioType.FinancingLoan or
            SimulationScenarioType.CashDebt or
            SimulationScenarioType.RecurringPayment;
        IsFinancing = type == SimulationScenarioType.FinancingLoan;
        IsStrategyChange = type == SimulationScenarioType.PaymentStrategyChange;
        IsCardPayoff = type == SimulationScenarioType.CreditCardFullPayment;
        NeedsAmount = !IsStrategyChange && !IsCardPayoff;
        StartDateLabel = IsCardPayoff
            ? "Tam ödeme tarihi"
            : "Başlangıç / işlem tarihi";
        IsRegularScenario = !IsStrategyChange;
        ScenarioDescription = type switch
        {
            SimulationScenarioType.CashPurchase =>
                "Tutar, seçtiğin tarihte finansal durumundan düşer.",
            SimulationScenarioType.CreditCardSinglePayment =>
                "Harcama, kartının ekstre kesim ve son ödeme tarihlerine göre hesaplanır.",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                "Taksitler ilgili kart ekstrelerine yansıtılır.",
            SimulationScenarioType.FinancingLoan =>
                "Toplam geri ödeme, ilk ödeme tarihinden başlayarak taksitlere bölünür.",
            SimulationScenarioType.CashDebt =>
                "Borç tutarı, seçtiğin ödeme sayısına kuruş farkı bırakmadan bölünür.",
            SimulationScenarioType.FutureOneTimePayment =>
                "Ödeme, seçtiğin tarihte zorunlu ödemelere eklenir.",
            SimulationScenarioType.RecurringPayment =>
                "Girilen tutar, belirtilen dönem sayısı boyunca aylık tekrarlanır.",
            SimulationScenarioType.FutureIncome =>
                "Gelir, seçtiğin tarihin dahil olduğu maaş dönemine eklenir.",
            SimulationScenarioType.SalaryChange =>
                "Yeni maaş, seçtiğin tarihten itibaren kullanılır.",
            SimulationScenarioType.PaymentStrategyChange =>
                "Yeni düzen yalnızca seçtiğin maaştan itibaren hesaplanır; Simülasyon Yap finans kayıtlarını değiştirmez.",
            SimulationScenarioType.CreditCardFullPayment =>
                "Seçilen tarihte ekstrenin tamamı ödenir; sonraki dönemlerde kart faizi ve nakit akışı yeniden projekte edilir.",
            _ => string.Empty
        };
    }

    partial void OnNameChanged(string value) { }
    partial void OnAmountChanged(string value) { }
    partial void OnSelectedCreditCardChanged(SelectionOption<Guid>? value) =>
        _ = value;
    partial void OnStartDateChanged(DateTime value) { }
    partial void OnPaymentCountChanged(string value) { }
    partial void OnFirstPaymentDateChanged(DateTime value) =>
        _ = value;
    partial void OnTotalRepaymentAmountChanged(string value) =>
        _ = value;
    partial void OnSelectedStrategyModeChanged(
        SelectionOption<PaymentAssignmentMode>? value) =>
        _ = value;
    partial void OnSelectedStrategySalaryDateChanged(
        SelectionOption<DateOnly>? value) =>
        _ = value;

    [RelayCommand]
    private void AddCondition()
    {
        try
        {
            SetStatus(string.Empty);
            var request = BuildRequest();
            SimulationCalculator.Validate(request);
            var condition = CreateConditionView(request);
            if (_editingConditionId is Guid editingId)
            {
                var index = DraftConditions
                    .ToList()
                    .FindIndex(x => x.Id == editingId);
                if (index < 0 || index >= DraftConditions.Count)
                {
                    throw new InvalidOperationException(
                        "Düzenlenecek koşul bulunamadı.");
                }

                DraftConditions[index] = condition;
                _editingConditionId = null;
            }
            else
            {
                DraftConditions.Add(condition);
            }

            ResetConditionForm();
            MarkResultsStale();
            NotifyDraftChanged();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private void EditCondition(SimulationDraftConditionView? condition)
    {
        if (condition is null)
        {
            return;
        }

        LoadConditionIntoForm(condition.Request);
        _editingConditionId = condition.Id;
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
    }

    [RelayCommand]
    private void RemoveCondition(SimulationDraftConditionView? condition)
    {
        if (condition is null)
        {
            return;
        }

        DraftConditions.Remove(condition);
        if (_editingConditionId == condition.Id)
        {
            _editingConditionId = null;
            OnPropertyChanged(nameof(IsEditingCondition));
            OnPropertyChanged(nameof(AddConditionButtonText));
        }

        MarkResultsStale();
        NotifyDraftChanged();
    }

    [RelayCommand]
    private void ClearDraft()
    {
        DraftConditions.Clear();
        _editingConditionId = null;
        Results.Clear();
        NarrativeInsights.Clear();
        SummaryMetrics.Clear();
        HasResults = false;
        IsResultStale = false;
        ResetApplyState(clearRequest: true);
        NotifyDraftChanged();
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
        SetStatus("Simülasyon planı temizlendi.");
    }

    [RelayCommand]
    private async Task CalculateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            if (DraftConditions.Count == 0)
            {
                SetStatus("Önce denemek istediğin en az bir koşul ekle.");
                return;
            }

            var requests = DraftConditions.Select(x => x.Request).ToArray();
            var result = await service.SimulateAsync(requests);
            _lastRequests = requests;
            IsPlanApplied = false;
            ApplyButtonText = "Planı Uygula";
            LastApplyResult = null;
            ApplyConfirmationText = BuildApplyConfirmation(requests);
            Populate(result);
            HasResults = true;
            IsResultStale = false;
        }
        catch (Exception exception)
        {
            if (!HasResults)
            {
                HasResults = false;
            }
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<SimulationApplyResult?> ApplyLastPlanAsync()
    {
        if (_lastRequests.Count == 0 || !HasResults)
        {
            SetStatus("Önce Simülasyon Yap ile sonucu hesaplamalısın.");
            return null;
        }

        if (IsResultStale)
        {
            SetStatus("Plan değişti. Uygulamadan önce simülasyonu güncelle.");
            return null;
        }

        if (IsPlanApplied)
        {
            SetStatus("Plan zaten uygulandı.");
            return LastApplyResult;
        }

        if (!await _applyLock.WaitAsync(0))
        {
            return null;
        }

        try
        {
            IsApplyingPlan = true;
            var result = await service.ApplySimulationAsync(
                _lastRequests,
                confirmed: true);
            LastApplyResult = result;
            IsPlanApplied = true;
            ApplyButtonText = "Plan Uygulandı";
            DraftConditions.Clear();
            Results.Clear();
            NarrativeInsights.Clear();
            SummaryMetrics.Clear();
            HasResults = false;
            IsResultStale = false;
            _lastRequests = [];
            NotifyDraftChanged();
            SetStatus(result.Message);
            return result;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return null;
        }
        finally
        {
            IsApplyingPlan = false;
            _applyLock.Release();
        }
    }

    private SimulationRequest BuildRequest()
    {
        var type = SelectedScenarioType?.Value
            ?? throw new InvalidOperationException("Plan türü seçmelisin.");
        var count = NeedsPaymentCount
            ? int.TryParse(PaymentCount, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Ödeme sayısı geçerli olmalıdır.")
            : 1;
        decimal? repayment = IsFinancing
            ? ParseMoney(TotalRepaymentAmount, "Toplam geri ödeme")
            : null;
        return new SimulationRequest(
            type,
            Name,
            IsStrategyChange || IsCardPayoff
                ? 0m
                : ParseMoney(Amount, "Tutar"),
            IsStrategyChange
                ? SelectedStrategySalaryDate?.Value ??
                  throw new InvalidOperationException(
                      "Planın başlayacağı maaşı seçmelisin.")
                : DateOnly.FromDateTime(StartDate),
            count,
            NeedsFirstPayment
                ? DateOnly.FromDateTime(FirstPaymentDate)
                : null,
            IsCard
                ? SelectedCreditCard?.Value ??
                  throw new InvalidOperationException("Bir kredi kartı seçmelisin.")
                : null,
            repayment,
            IsStrategyChange ? SelectedStrategyMode?.Value : null,
            IsStrategyChange ? SelectedStrategySalaryDate?.Value : null,
            _editingConditionId ?? Guid.NewGuid());
    }

    private string BuildApplyConfirmation(IReadOnlyList<SimulationRequest> requests)
    {
        if (requests.Count == 1)
        {
            return BuildApplyConfirmation(requests[0]);
        }

        var preview = string.Join(
            Environment.NewLine,
            DraftConditions.Take(6).Select(x => $"• {x.DateText} — {x.SummaryText}"));
        return
            $"Bu simülasyon planındaki {requests.Count} koşul gerçek finans planına birlikte eklenecek.\n\n{preview}\n\nHer şey tek seferde kaydedilir; bir koşul kaydedilemezse hiçbir değişiklik yapılmaz.";
    }

    private string BuildApplyConfirmation(SimulationRequest request)
    {
        var summary = request.Type is
            SimulationScenarioType.PaymentStrategyChange or
            SimulationScenarioType.CreditCardFullPayment
                ? request.Name.Trim()
                : $"{Money(request.Amount)} {request.Name.Trim()}";
        var detail = request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"Kart: {CardLabel(request.CreditCardId)}\n{request.PaymentCount} taksit\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"Kart: {CardLabel(request.CreditCardId)}\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardFullPayment =>
                $"Kart: {CardLabel(request.CreditCardId)}\nTam ödeme: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.FinancingLoan =>
                $"{request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}\nİlk ödeme: {request.FirstPaymentDate:dd MMMM yyyy}",
            SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                $"{request.PaymentCount} ödeme\nİlk ödeme: {request.FirstPaymentDate:dd MMMM yyyy}",
            SimulationScenarioType.PaymentStrategyChange =>
                $"Başlangıç maaşı: {request.EffectiveSalaryDate:dd MMMM yyyy}",
            _ => $"Tarih: {request.StartDate:dd MMMM yyyy}"
        };
        return $"Bu plan gerçek finans planına eklenecek.\n\n{summary}\n{detail}";
    }

    private SimulationDraftConditionView CreateConditionView(
        SimulationRequest request)
    {
        var date = request.Type == SimulationScenarioType.PaymentStrategyChange
            ? request.EffectiveSalaryDate ?? request.StartDate
            : request.StartDate;
        return new SimulationDraftConditionView(
            request.ScenarioId,
            request,
            date.ToString("MMMM yyyy", TurkishCulture),
            ConditionTypeText(request.Type),
            ConditionSummaryText(request));
    }

    private string ConditionSummaryText(SimulationRequest request)
    {
        var amount = Money(request.Amount);
        return request.Type switch
        {
            SimulationScenarioType.CashPurchase =>
                $"{amount} • Nakit satın alma",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"{CardLabel(request.CreditCardId)} • {amount} • Tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                $"{CardLabel(request.CreditCardId)} • {amount} • {request.PaymentCount} taksit",
            SimulationScenarioType.FinancingLoan =>
                $"{amount} • {request.PaymentCount} taksit • toplam {Money(request.TotalRepaymentAmount.GetValueOrDefault())}",
            SimulationScenarioType.CashDebt =>
                $"{amount} • {request.PaymentCount} ödeme",
            SimulationScenarioType.FutureOneTimePayment =>
                $"{amount} • Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment =>
                $"{amount}/ay • {request.PaymentCount} ay",
            SimulationScenarioType.FutureIncome =>
                $"{amount} • Tek seferlik gelir",
            SimulationScenarioType.SalaryChange =>
                $"{amount} • Yeni maaş",
            SimulationScenarioType.PaymentStrategyChange =>
                $"{StrategyModeLabel(request.NewPaymentAssignmentMode)} • {request.EffectiveSalaryDate:dd MMMM yyyy} maaşı",
            SimulationScenarioType.CreditCardFullPayment =>
                $"{CardLabel(request.CreditCardId)} • Ekstre tam ödeme",
            _ => request.Name
        };
    }

    private static string ConditionTypeText(SimulationScenarioType type) =>
        type switch
        {
            SimulationScenarioType.CashPurchase => "Nakit satın alma",
            SimulationScenarioType.CreditCardSinglePayment => "Karttan tek çekim",
            SimulationScenarioType.CreditCardInstallmentPurchase => "Kart taksitli harcama",
            SimulationScenarioType.FinancingLoan => "Finansman / kredi",
            SimulationScenarioType.CashDebt => "Nakit borç",
            SimulationScenarioType.FutureOneTimePayment => "Tek seferlik ödeme",
            SimulationScenarioType.RecurringPayment => "Düzenli ödeme",
            SimulationScenarioType.FutureIncome => "Tek seferlik gelir",
            SimulationScenarioType.SalaryChange => "Maaş değişikliği",
            SimulationScenarioType.PaymentStrategyChange => "Maaş kullanım düzeni",
            SimulationScenarioType.CreditCardFullPayment => "Kart ekstresini kapat",
            _ => "Koşul"
        };

    private string CardLabel(Guid? creditCardId) =>
        CreditCards.FirstOrDefault(x => x.Value == creditCardId)?.Label ??
        "Kart";

    private static string StrategyModeLabel(PaymentAssignmentMode? mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";

    private void LoadConditionIntoForm(SimulationRequest request)
    {
        SelectedScenarioType = ScenarioTypes.First(x => x.Value == request.Type);
        Name = request.Name;
        Amount = request.Amount > 0m
            ? request.Amount.ToString("0.##", TurkishCulture)
            : string.Empty;
        StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        PaymentCount = request.PaymentCount.ToString(TurkishCulture);
        FirstPaymentDate = (request.FirstPaymentDate ?? request.StartDate)
            .ToDateTime(TimeOnly.MinValue);
        TotalRepaymentAmount = request.TotalRepaymentAmount is decimal repayment
            ? repayment.ToString("0.##", TurkishCulture)
            : string.Empty;
        SelectedCreditCard = CreditCards.FirstOrDefault(x =>
            x.Value == request.CreditCardId);
        SelectedStrategyMode = request.NewPaymentAssignmentMode is { } mode
            ? StrategyModes.First(x => x.Value == mode)
            : SelectedStrategyMode;
        SelectedStrategySalaryDate = request.EffectiveSalaryDate is { } date
            ? StrategySalaryDates.FirstOrDefault(x => x.Value == date)
            : SelectedStrategySalaryDate;
    }

    private void ResetConditionForm()
    {
        _editingConditionId = null;
        Name = "Yeni koşul";
        Amount = string.Empty;
        PaymentCount = "1";
        StartDate = DateTime.Today;
        FirstPaymentDate = DateTime.Today.AddMonths(1);
        TotalRepaymentAmount = string.Empty;
        SelectedScenarioType = ScenarioTypes[0];
        SelectedCreditCard = CreditCards.FirstOrDefault();
        SelectedStrategySalaryDate = StrategySalaryDates.FirstOrDefault();
        OnPropertyChanged(nameof(IsEditingCondition));
        OnPropertyChanged(nameof(AddConditionButtonText));
    }

    private void ResetApplyState(bool clearRequest)
    {
        if (clearRequest)
        {
            _lastRequests = [];
        }

        LastApplyResult = null;
        IsPlanApplied = false;
        IsApplyingPlan = false;
        ApplyButtonText = "Planı Uygula";
    }

    private void MarkResultsStale()
    {
        if (HasResults && !IsPlanApplied)
        {
            IsResultStale = true;
        }

        ResetApplyState(clearRequest: true);
    }

    private void NotifyDraftChanged()
    {
        OnPropertyChanged(nameof(HasDraftConditions));
        OnPropertyChanged(nameof(HasNoDraftConditions));
        OnPropertyChanged(nameof(HasMultipleDraftConditions));
        OnPropertyChanged(nameof(CanRunSimulation));
        OnPropertyChanged(nameof(DraftConditionCountText));
        OnPropertyChanged(nameof(RunSimulationButtonText));
        OnPropertyChanged(nameof(CanApplyPlan));
    }

    private void Populate(SimulationResult result)
    {
        var projectionSummary = simulatorInsightService.Build(result.Scenario);
        var baselineEnding = result.Baseline[^1].EndingProjectedSavings;
        var scenarioEnding = result.Risk.EndingProjectedSavings;
        BaselineEnding = Money(baselineEnding);
        ScenarioEnding = Money(scenarioEnding);
        EndingDifference = Money(scenarioEnding - baselineEnding);
        AssignmentModeText = AssignmentModeLabel(
            result.Scenario[0].PaymentAssignmentMode);
        TightestPeriod = SalaryText(result.Risk.LowestPeriod.Start);
        LowestAvailable = Money(result.Risk.LowestAvailableAfterMandatory);
        LowestSavingsCapacity = Money(result.Risk.LowestSavingsCapacity);
        LowestProjectedSavings = Money(result.Risk.LowestProjectedSavings);
        FirstNegativePeriod =
            result.Risk.FirstDeficitPeriod is { } negative
                ? SalaryText(negative.Start)
                : "12 aylık görünümde finansman açığı oluşmuyor.";
        MaximumCarryOverDeficit = Money(
            result.Risk.MaximumCarryOverDeficit);
        RecoveryPeriod = result.Risk.RecoveryPeriod is { } recovery
            ? SalaryText(recovery.Start)
            : result.Risk.MaximumCarryOverDeficit > 0m
                ? "Gösterilen dönemde kapanmıyor"
                : "Gerekmedi";
        TotalScenarioCost = Money(result.Risk.TotalScenarioCost);
        var monthlyBurden = ResolveMonthlyBurden(_lastRequests, result);
        HasMonthlyBurden = monthlyBurden is not null;
        MonthlyBurden = monthlyBurden is decimal burden
            ? Money(burden)
            : string.Empty;
        HasFinancingCost = result.Risk.FinancingCost is not null;
        FinancingCost = result.Risk.FinancingCost is decimal cost
            ? Money(cost)
            : string.Empty;
        BaselineInterest = Money(
            result.BaselineInterest.TotalInterestCost);
        ScenarioInterest = Money(
            result.ScenarioInterest.TotalInterestCost);
        InterestDifferenceTitle = result.AdditionalInterestCost < 0m
            ? "Faiz Tasarrufu"
            : "Ek Faiz Yükü";
        InterestDifference = Money(
            result.AdditionalInterestCost < 0m
                ? result.InterestSaving
                : result.AdditionalInterestCost);
        FriendlySummary = string.Join(Environment.NewLine,
            projectionSummary.NarrativeInsights);
        var transition = result.Scenario.FirstOrDefault(x =>
            x.IsStrategyTransition);
        HasStrategyTransitionSummary = transition is not null;
        StrategyTransitionSummary = transition is null
            ? string.Empty
            : string.Join(Environment.NewLine,
                $"Geçiş maaşı: {SalaryText(transition.PeriodStart)}",
                $"Normal zorunlu ödemeler: {Money(result.Baseline.Single(x => x.PeriodStart == transition.PeriodStart).MandatoryOutflow)}",
                $"Geçmiş düzenden kapanacak: {Money(transition.TransitionCatchUpAmount)}",
                $"İleri dönem için ayrılacak: {Money(transition.ForwardFundedAmount)}",
                $"Toplam geçiş yükü: {Money(transition.MandatoryOutflow)}",
                $"Dönem neti: {Money(transition.EstimatedSavingsCapacity)}",
                $"Dönem sonu durumu: {Money(transition.EndingProjectedSavings)}");

        NarrativeInsights.Clear();
        foreach (var insight in projectionSummary.NarrativeInsights)
        {
            NarrativeInsights.Add(insight);
        }

        SummaryMetrics.Clear();
        foreach (var metric in projectionSummary.KeyMetrics)
        {
            SummaryMetrics.Add(metric);
        }

        Results.Clear();
        foreach (var row in projectionSummary.Periods)
        {
            Results.Add(row);
        }
    }

    private static string SalaryText(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Maaş kullanımı: Geçmiş dönemi kapatırım"
            : "Maaş kullanımı: Gelecek dönemi karşılarım";

    private static decimal? ResolveMonthlyBurden(
        IReadOnlyList<SimulationRequest> requests,
        SimulationResult result)
    {
        var request = requests.Count == 1 ? requests[0] : null;
        if (request is null || request.PaymentCount <= 1)
        {
            return null;
        }

        return request.Type switch
        {
            SimulationScenarioType.CreditCardInstallmentPurchase or
                SimulationScenarioType.FinancingLoan or
                SimulationScenarioType.CashDebt or
                SimulationScenarioType.RecurringPayment =>
                result.Risk.TotalScenarioCost / request.PaymentCount,
            _ => null
        };
    }

    private static string AssignmentText(SalaryPeriodProjection row)
    {
        var action = row.PaymentAssignmentMode ==
                     PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

    private static string SignedMoney(decimal value)
    {
        var formatted = Money(value);
        return value > 0m ? $"+{formatted}" : formatted;
    }
}
