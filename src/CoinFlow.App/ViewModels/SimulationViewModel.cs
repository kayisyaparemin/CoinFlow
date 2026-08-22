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
    CoinFlowService service) : ViewModelBase
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
    public ObservableCollection<SimulationLine> Results { get; } = [];
    public ObservableCollection<SelectionOption<DateOnly>> StrategySalaryDates { get; } = [];
    public IReadOnlyList<SelectionOption<PaymentAssignmentMode>> StrategyModes { get; } =
    [
        new("Geçmiş dönemi kapatırım", PaymentAssignmentMode.PreviousPeriod),
        new("Gelecek dönemi karşılarım", PaymentAssignmentMode.UpcomingPeriod)
    ];

    private SimulationRequest? _lastRequest;
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
    private bool hasResults;
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
    [ObservableProperty] private bool isPlanAvailable;
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

    public bool CanApplyPlan =>
        HasResults && !IsApplyingPlan && !IsPlanApplied;

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
            HasResults = false;
            ResetApplyState(clearRequest: true);
            if (!IsPlanAvailable)
            {
                EmptyStateMessage = plan.Salaries.Count == 0
                    ? "Simülasyon yapabilmek için önce temel finans planını oluştur."
                    : "Simülasyon için maaş kullanım düzenini seçerek finans planını tamamla.";
                AssignmentModeText = string.Empty;
                CreditCards.Clear();
                StrategySalaryDates.Clear();
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
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private async Task OpenPeriodDetailAsync(SimulationLine? line)
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
                        line.Impact.Scenario,
                        line.Impact.Baseline)
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
                "Seçilen tarihte ekstrenin tamamı ödenir; sonraki kart faizi ve faiz tasarrufu Mevcut Plan ile karşılaştırılır.",
            _ => string.Empty
        };
        HasResults = false;
        ResetApplyState(clearRequest: true);
    }

    partial void OnNameChanged(string value) => InvalidateCalculatedPlan();
    partial void OnAmountChanged(string value) => InvalidateCalculatedPlan();
    partial void OnSelectedCreditCardChanged(SelectionOption<Guid>? value) =>
        InvalidateCalculatedPlan();
    partial void OnStartDateChanged(DateTime value) => InvalidateCalculatedPlan();
    partial void OnPaymentCountChanged(string value) => InvalidateCalculatedPlan();
    partial void OnFirstPaymentDateChanged(DateTime value) =>
        InvalidateCalculatedPlan();
    partial void OnTotalRepaymentAmountChanged(string value) =>
        InvalidateCalculatedPlan();
    partial void OnSelectedStrategyModeChanged(
        SelectionOption<PaymentAssignmentMode>? value) =>
        InvalidateCalculatedPlan();
    partial void OnSelectedStrategySalaryDateChanged(
        SelectionOption<DateOnly>? value) =>
        InvalidateCalculatedPlan();

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
            var request = BuildRequest();
            var result = await service.SimulateAsync(request);
            _lastRequest = request;
            IsPlanApplied = false;
            ApplyButtonText = "Planı Uygula";
            LastApplyResult = null;
            ApplyConfirmationText = BuildApplyConfirmation(request);
            Populate(result);
            HasResults = true;
        }
        catch (Exception exception)
        {
            HasResults = false;
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<SimulationApplyResult?> ApplyLastPlanAsync()
    {
        if (_lastRequest is null || !HasResults)
        {
            SetStatus("Önce Simülasyon Yap ile sonucu hesaplamalısın.");
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
                _lastRequest,
                confirmed: true);
            LastApplyResult = result;
            IsPlanApplied = true;
            ApplyButtonText = "Plan Uygulandı";
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
            Guid.NewGuid());
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
                $"Kart: {SelectedCreditCard?.Label}\n{request.PaymentCount} taksit\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardSinglePayment =>
                $"Kart: {SelectedCreditCard?.Label}\nİşlem: {request.StartDate:dd MMMM yyyy}",
            SimulationScenarioType.CreditCardFullPayment =>
                $"Kart: {SelectedCreditCard?.Label}\nTam ödeme: {request.StartDate:dd MMMM yyyy}",
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

    private void ResetApplyState(bool clearRequest)
    {
        if (clearRequest)
        {
            _lastRequest = null;
        }

        LastApplyResult = null;
        IsPlanApplied = false;
        IsApplyingPlan = false;
        ApplyButtonText = "Planı Uygula";
    }

    private void InvalidateCalculatedPlan()
    {
        if (!HasResults && _lastRequest is null)
        {
            return;
        }

        HasResults = false;
        ResetApplyState(clearRequest: true);
    }

    private void Populate(SimulationResult result)
    {
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
        var monthlyBurden = ResolveMonthlyBurden(_lastRequest, result);
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
        FriendlySummary = result.FriendlySummary;
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

        Results.Clear();
        foreach (var row in result.Rows)
        {
            Results.Add(new SimulationLine(
                row,
                SalaryText(row.Scenario.PeriodStart),
                AssignmentText(row.Scenario),
                Money(row.Baseline.EndingProjectedSavings),
                Money(row.Scenario.EndingProjectedSavings),
                SignedMoney(row.ProjectedSavingsDifference),
                row.ProjectedSavingsDifference < 0m,
                SignedMoney(row.InterestDifference),
                row.InterestDifference != 0m));
        }
    }

    private static string SalaryText(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Maaş kullanımı: Geçmiş dönemi kapatırım"
            : "Maaş kullanımı: Gelecek dönemi karşılarım";

    private static decimal? ResolveMonthlyBurden(
        SimulationRequest? request,
        SimulationResult result)
    {
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
