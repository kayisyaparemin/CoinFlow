using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<SelectionOption<CreditCardPaymentType>> UpcomingPaymentTypes { get; } =
    [
        new("Asgariyi öde", CreditCardPaymentType.Minimum),
        new("Ekstrenin tamamını öde", CreditCardPaymentType.FullStatement),
        new("Özel tutar gir", CreditCardPaymentType.FixedAmount)
    ];

    private Guid? _upcomingCardId;
    private DateOnly? _upcomingDueDate;
    private decimal? _upcomingMinimumAmount;

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string currentAvailable = "—";
    [ObservableProperty] private string daysRemaining = "—";
    [ObservableProperty] private string dailyReward = "—";
    [ObservableProperty] private string sustainableDaily = "—";
    [ObservableProperty] private string coinPool = "—";
    [ObservableProperty] private string encouragement = string.Empty;
    [ObservableProperty] private string emergencyFund = "—";
    [ObservableProperty] private bool hasCurrentActual;
    [ObservableProperty] private bool needsSnapshot;
    [ObservableProperty] private double progress;

    [ObservableProperty] private string nextPeriodText = "—";
    [ObservableProperty] private string nextSalary = "—";
    [ObservableProperty] private string nextObligations = "—";
    [ObservableProperty] private string nextBudget = "—";
    [ObservableProperty] private string nextDailyCoin = "—";

    [ObservableProperty] private bool hasUpcomingCardPayment;
    [ObservableProperty] private string upcomingCardName = "—";
    [ObservableProperty] private string upcomingStatementAmount = "—";
    [ObservableProperty] private string upcomingMinimumPayment = "—";
    [ObservableProperty] private string upcomingDueDate = "—";
    [ObservableProperty] private string upcomingPlan = "Henüz seçilmedi";
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedUpcomingPaymentType;
    [ObservableProperty] private string upcomingCustomAmount = string.Empty;
    [ObservableProperty] private bool isUpcomingCustomPayment;

    [ObservableProperty] private string correctionAmount = string.Empty;
    [ObservableProperty] private DateTime correctionDate = DateTime.Today;
    [ObservableProperty] private string correctionNote = string.Empty;

    [ObservableProperty] private string calculationDetails = string.Empty;
    public bool IsDevelopment => BuildInfo.IsDevelopment;

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
            var currentStart = snapshot.SalaryPeriod.Period.Start;
            var currentEnd = snapshot.SalaryPeriod.Period.End;
            CurrentPeriodText = PeriodText(currentStart, currentEnd);
            HasCurrentActual = snapshot.DailyCoin.HasCurrentActual;
            NeedsSnapshot = !HasCurrentActual;
            CurrentAvailable = HasCurrentActual ? Money(snapshot.DailyCoin.RemainingBudget) : "Henüz girilmedi";
            DaysRemaining = $"{snapshot.DailyCoin.RemainingDays} gün";
            DailyReward = HasCurrentActual ? Money(snapshot.DailyCoin.BaseDailyCoin, 2) : "—";
            SustainableDaily = HasCurrentActual ? Money(snapshot.DailyCoin.SustainableDailyBudget, 2) : "—";
            CoinPool = HasCurrentActual ? Money(snapshot.DailyCoin.CoinPool, 2) : "—";
            Encouragement = snapshot.Encouragement;
            EmergencyFund = $"{Money(snapshot.EmergencyFund.CurrentAmount)} / {Money(snapshot.EmergencyFund.TargetAmount)}";
            Progress = Math.Clamp((double)snapshot.DailyCoin.ProgressRate, 0d, 1d);

            var next = snapshot.NextSalaryPeriod;
            NextPeriodText = PeriodText(next.Period.Start, next.Period.End);
            NextSalary = Money(next.Salary);
            NextObligations = next.HasUndeterminedCardPayments ? "Kesin değil" : Money(next.TotalObligations, 2);
            NextBudget = next.HasUndeterminedCardPayments ? "Kesin değil" : Money(next.ProjectedSpendable, 2);
            NextDailyCoin = next.HasUndeterminedCardPayments ? "Kesin değil" : Money(next.ProjectedDailyCoin, 2);

            var upcoming = snapshot.UpcomingCardPayment;
            HasUpcomingCardPayment = upcoming is not null;
            if (upcoming is not null)
            {
                _upcomingCardId = upcoming.CardId;
                _upcomingDueDate = upcoming.PaymentDueDate;
                _upcomingMinimumAmount = upcoming.MinimumPayment;
                UpcomingCardName = upcoming.CardName;
                UpcomingStatementAmount = MoneyOrDash(upcoming.StatementBalance);
                UpcomingMinimumPayment = MoneyOrDash(upcoming.MinimumPayment);
                UpcomingDueDate = upcoming.PaymentDueDate.ToString("dd MMMM yyyy", TurkishCulture);
                UpcomingPlan = PaymentPlanText(upcoming.Resolution, upcoming.PaymentType, upcoming.PlannedPayment);
                SelectedUpcomingPaymentType = upcoming.PaymentType is null ||
                                              upcoming.Resolution == CreditCardPaymentResolution.ProjectionFallback
                    ? UpcomingPaymentTypes[0]
                    : UpcomingPaymentTypes.First(x => x.Value == upcoming.PaymentType.Value);
            }
            else
            {
                _upcomingCardId = null;
                _upcomingDueDate = null;
                _upcomingMinimumAmount = null;
            }

            var details = snapshot.Details;
            CalculationDetails =
                $"Current period: {details.CurrentPeriod.Start:dd.MM.yyyy} → {details.CurrentPeriod.End:dd.MM.yyyy}\n" +
                $"Balance source: {details.BalanceSource}\n" +
                $"Snapshot/start: {(details.SnapshotDate is null ? "—" : details.SnapshotDate.Value.ToString("dd.MM.yyyy"))} • {Money(details.SnapshotOrStartAmount, 2)}\n" +
                $"Eligible spending: {Money(details.EligibleSpending, 2)}\n" +
                $"Current available: {Money(details.CurrentAvailable, 2)}\n" +
                $"Remaining days: {details.RemainingDays} • Sustainable: {Money(details.SustainableDaily, 2)}\n" +
                $"Next card: {(details.NextCardStatementClose is null ? "—" : details.NextCardStatementClose.Value.ToString("dd.MM.yyyy"))} close → " +
                $"{(details.NextCardPaymentDue is null ? "—" : details.NextCardPaymentDue.Value.ToString("dd.MM.yyyy"))} due\n" +
                $"Statement: {MoneyOrDash(details.NextCardStatementBalance)} • Minimum: {MoneyOrDash(details.NextCardMinimumPayment)} • " +
                $"Payment: {MoneyOrDash(details.NextCardPayment)} • Resolution: {details.NextCardPaymentResolution?.ToString() ?? "—"}";
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

    partial void OnSelectedUpcomingPaymentTypeChanged(SelectionOption<CreditCardPaymentType>? value) =>
        IsUpcomingCustomPayment = value?.Value == CreditCardPaymentType.FixedAmount;

    [RelayCommand]
    private async Task SaveUpcomingCardPaymentAsync()
    {
        if (_upcomingCardId is null || _upcomingDueDate is null)
        {
            SetStatus("Planlanacak yaklaşan kart ödemesi bulunmuyor.");
            return;
        }

        var warning = string.Empty;
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var paymentType = SelectedUpcomingPaymentType?.Value
                ?? throw new InvalidOperationException("Ödeme şekli seçilmelidir.");
            decimal? amount = null;
            if (paymentType == CreditCardPaymentType.FixedAmount)
            {
                amount = ParseMoney(UpcomingCustomAmount, "Özel ödeme tutarı");
                if (amount <= 0m)
                {
                    throw new InvalidOperationException("Özel ödeme tutarı sıfırdan büyük olmalıdır.");
                }

                if (_upcomingMinimumAmount is decimal minimum && amount < minimum)
                {
                    warning = $"Girdiğin {Money(amount.Value, 2)} bu ekstredeki {Money(minimum, 2)} asgari ödemenin altında. Projeksiyonda en az asgari tutar kullanıldı.";
                }
            }

            await service.SaveCreditCardPaymentPlanAsync(
                _upcomingCardId.Value,
                _upcomingDueDate.Value,
                paymentType,
                amount);
            UpcomingCustomAmount = string.Empty;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
        SetStatus(string.IsNullOrWhiteSpace(warning) ? "Bu ekstreye özel ödeme planı kaydedildi." : warning);
    }

    [RelayCommand]
    private async Task DeferUpcomingCardPaymentAsync()
    {
        if (_upcomingCardId is null || _upcomingDueDate is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await service.RemoveCreditCardPaymentPlanAsync(_upcomingCardId.Value, _upcomingDueDate.Value);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
        SetStatus("Bu ekstreye özel karar kaldırıldı; kartın genel stratejisi geçerli.");
    }

    [RelayCommand]
    private async Task CorrectBalanceAsync()
    {
        var saved = false;
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            await service.SaveSpendableBalanceSnapshotAsync(
                ParseMoney(CorrectionAmount, "Serbest bakiye"),
                DateOnly.FromDateTime(CorrectionDate),
                CorrectionNote);
            CorrectionAmount = string.Empty;
            CorrectionNote = string.Empty;
            saved = true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (saved)
        {
            await LoadAsync();
            SetStatus("Serbest bakiye güncellendi. Bu andan sonraki nakit harcamalar yeni bakiyeden düşülecek.");
        }
    }

    private static string PeriodText(DateOnly start, DateOnly end) =>
        $"{start:dd MMM yyyy} → {end:dd MMM yyyy}".ToUpper(TurkishCulture);

    private static string MoneyOrDash(decimal? value) =>
        value is null ? "Henüz hesaplanamadı" : Money(value.Value, 2);

    private static string PaymentPlanText(
        CreditCardPaymentResolution resolution,
        CreditCardPaymentType? paymentType,
        decimal? effectivePayment)
    {
        if (resolution is CreditCardPaymentResolution.Undetermined or
            CreditCardPaymentResolution.ProjectionFallback)
        {
            return "Henüz seçilmedi";
        }

        var label = paymentType switch
        {
            CreditCardPaymentType.Minimum => "Asgari ödeme",
            CreditCardPaymentType.FullStatement => "Ekstrenin tamamı",
            CreditCardPaymentType.FixedAmount => "Özel tutar",
            _ => "Ödeme"
        };
        return effectivePayment is null ? label : $"{label} • {Money(effectivePayment.Value, 2)}";
    }
}
