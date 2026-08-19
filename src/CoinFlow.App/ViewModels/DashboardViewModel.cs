using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(CoinFlowService service) : ViewModelBase
{
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
            NextObligations = Money(next.TotalObligations, 2);
            NextBudget = Money(next.ProjectedSpendable, 2);
            NextDailyCoin = Money(next.ProjectedDailyCoin, 2);

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
                $"Statement: {Money(details.NextCardStatementBalance, 2)} • Payment: {Money(details.NextCardPayment, 2)}";
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
}
