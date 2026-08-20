using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
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
        new("Gelecek toplu ödeme", SimulationScenarioType.FutureOneTimePayment),
        new("Dönemsel ödeme", SimulationScenarioType.RecurringPayment),
        new("Gelecek gelir", SimulationScenarioType.FutureIncome),
        new("Maaş değişikliği", SimulationScenarioType.SalaryChange)
    ];

    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];
    public ObservableCollection<SimulationLine> Results { get; } = [];

    private SimulationRequest? _lastRequest;

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
    [ObservableProperty] private string scenarioDescription = string.Empty;
    [ObservableProperty] private bool hasResults;
    [ObservableProperty] private string baselineEnding = "—";
    [ObservableProperty] private string scenarioEnding = "—";
    [ObservableProperty] private string endingDifference = "—";
    [ObservableProperty] private string tightestPeriod = "—";
    [ObservableProperty] private string lowestAvailable = "—";
    [ObservableProperty] private string lowestSavingsCapacity = "—";
    [ObservableProperty] private string lowestProjectedSavings = "—";
    [ObservableProperty] private string firstNegativePeriod = "Yok";
    [ObservableProperty] private string totalScenarioCost = "—";
    [ObservableProperty] private string financingCost = string.Empty;
    [ObservableProperty] private bool hasFinancingCost;
    [ObservableProperty] private string friendlySummary = string.Empty;
    [ObservableProperty] private string assignmentModeText = string.Empty;

    public async Task LoadAsync()
    {
        var plan = await service.GetFinancialPlanAsync();
        CreditCards.Clear();
        foreach (var card in plan.CreditCards)
        {
            CreditCards.Add(new SelectionOption<Guid>(
                $"{card.Bank} {card.Name}".Trim(),
                card.Id));
        }

        AssignmentModeText = AssignmentModeLabel(
            plan.Settings.PaymentAssignmentMode);

        SelectedScenarioType ??= ScenarioTypes[0];
        SelectedCreditCard ??= CreditCards.FirstOrDefault();
    }

    partial void OnSelectedScenarioTypeChanged(
        SelectionOption<SimulationScenarioType>? value)
    {
        var type = value?.Value ?? SimulationScenarioType.CashPurchase;
        IsCard = type is
            SimulationScenarioType.CreditCardSinglePayment or
            SimulationScenarioType.CreditCardInstallmentPurchase;
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
        ScenarioDescription = type switch
        {
            SimulationScenarioType.CashPurchase =>
                "Büyük nakit gideri exact tarihinde tahmini birikimden düşer.",
            SimulationScenarioType.CreditCardSinglePayment =>
                "Harcama gerçek kart kesim ve son ödeme tarihleriyle hesaplanır.",
            SimulationScenarioType.CreditCardInstallmentPurchase =>
                "Taksit posting tarihleri gerçek kart statement motoruna eklenir.",
            SimulationScenarioType.FinancingLoan =>
                "Toplam geri ödeme exact tarihlerle taksit planına dönüşür.",
            SimulationScenarioType.CashDebt =>
                "Faizsiz borç tutarı ödeme sayısına exact toplamla bölünür.",
            SimulationScenarioType.FutureOneTimePayment =>
                "Toplu ödeme exact tarihinde zorunlu ödemeye eklenir.",
            SimulationScenarioType.RecurringPayment =>
                "Girilen tutar, belirtilen dönem sayısı boyunca aylık tekrarlanır.",
            SimulationScenarioType.FutureIncome =>
                "Gelir exact tarihinde ilgili maaş dönemine eklenir.",
            SimulationScenarioType.SalaryChange =>
                "Yeni maaş, effective date dönem başlangıcına ulaştığında geçerli olur.",
            _ => string.Empty
        };
        HasResults = false;
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
            var request = BuildRequest();
            var result = await service.SimulateAsync(request);
            _lastRequest = request;
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

    public async Task<bool> ApplyLastPlanAsync()
    {
        if (_lastRequest is null || !HasResults)
        {
            SetStatus("Önce bir simülasyon hesaplayın.");
            return false;
        }

        try
        {
            await service.ApplySimulationAsync(
                _lastRequest,
                confirmed: true);
            SetStatus("Plan finansal kayıtlarına uygulandı.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    private SimulationRequest BuildRequest()
    {
        var type = SelectedScenarioType?.Value
            ?? throw new InvalidOperationException("Senaryo türü seçilmelidir.");
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
            ParseMoney(Amount, "Tutar"),
            DateOnly.FromDateTime(StartDate),
            count,
            NeedsFirstPayment
                ? DateOnly.FromDateTime(FirstPaymentDate)
                : null,
            IsCard
                ? SelectedCreditCard?.Value ??
                  throw new InvalidOperationException("Kredi kartı seçilmelidir.")
                : null,
            repayment);
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
            result.Risk.FirstNegativeProjectedSavingsPeriod is { } negative
                ? SalaryText(negative.Start)
                : result.Risk.FirstNegativeSavingsCapacityPeriod is { } capacity
                    ? SalaryText(capacity.Start)
                    : "Yok";
        TotalScenarioCost = Money(result.Risk.TotalScenarioCost);
        HasFinancingCost = result.Risk.FinancingCost is not null;
        FinancingCost = result.Risk.FinancingCost is decimal cost
            ? Money(cost)
            : string.Empty;
        FriendlySummary = result.FriendlySummary;

        Results.Clear();
        foreach (var row in result.Rows)
        {
            Results.Add(new SimulationLine(
                SalaryText(row.Scenario.PeriodStart),
                Money(row.Baseline.EndingProjectedSavings),
                Money(row.Scenario.EndingProjectedSavings),
                Money(row.ProjectedSavingsDifference),
                Money(row.Scenario.AvailableAfterMandatory),
                Money(row.Scenario.EstimatedSavingsCapacity)));
        }
    }

    private static string SalaryText(DateOnly salaryDate) =>
        $"{salaryDate.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";

    private static string AssignmentModeLabel(PaymentAssignmentMode mode) =>
        mode == PaymentAssignmentMode.PreviousPeriod
            ? "Maaş kullanımı: Geçmiş dönemi kapatırım"
            : "Maaş kullanımı: Gelecek dönemi karşılarım";
}
