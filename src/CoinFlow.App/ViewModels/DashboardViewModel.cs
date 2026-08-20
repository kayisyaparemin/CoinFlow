using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(
    CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<UpcomingPaymentLine>
        UpcomingPayments { get; } = [];

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string income = "—";
    [ObservableProperty] private string mandatory = "—";
    [ObservableProperty] private string available = "—";
    [ObservableProperty] private string living = "—";
    [ObservableProperty] private string estimatedSavings = "—";
    [ObservableProperty] private string endingSavings = "—";
    [ObservableProperty] private string twelveMonthSavings = "—";
    [ObservableProperty] private string tightestPeriod = "—";
    [ObservableProperty] private string tightestValue = "—";
    [ObservableProperty] private string deficitMessage = string.Empty;
    [ObservableProperty] private bool hasDeficit;
    [ObservableProperty] private bool hasUpcomingPayments;
    [ObservableProperty] private bool hasNoUpcomingPayments = true;
    [ObservableProperty] private bool hasUndeterminedCardPayment;
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
            var dashboard = await service.GetDashboardAsync();
            var current = dashboard.CurrentPeriod;

            CurrentPeriodText = PeriodText(current.Period);
            Income = Money(current.TotalIncome);
            Mandatory = Money(current.MandatoryOutflow);
            Available = Money(current.AvailableAfterMandatory);
            Living = Money(current.LivingBudget);
            EstimatedSavings = Money(current.EstimatedSavingsCapacity);
            EndingSavings = Money(current.EndingProjectedSavings);
            TwelveMonthSavings = Money(
                dashboard.TwelvePeriodEndingProjectedSavings);
            TightestPeriod = PeriodText(dashboard.TightestPeriod.Period);
            TightestValue = Money(
                dashboard.TightestPeriod.EstimatedSavingsCapacity);
            HasDeficit = current.EstimatedSavingsCapacity < 0m;
            DeficitMessage = HasDeficit
                ? $"Bu dönemde yaşam bütçesi sonrası {Money(Math.Abs(current.EstimatedSavingsCapacity))} finansman açığı oluşuyor."
                : $"Bu dönemin tahmini tasarruf kapasitesi {Money(current.EstimatedSavingsCapacity)}.";
            HasUndeterminedCardPayment =
                dashboard.HasUndeterminedCardPayments;

            UpcomingPayments.Clear();
            foreach (var payment in dashboard.UpcomingPayments)
            {
                UpcomingPayments.Add(new UpcomingPaymentLine(
                    payment.DueDate.ToString("dd MMM", TurkishCulture),
                    payment.Name,
                    Money(payment.Amount),
                    payment.IsEstimate
                        ? "Kart projeksiyonu • tahmini"
                        : payment.Type switch
                        {
                            ObligationType.Loan => "Kredi",
                            ObligationType.CreditCard => "Kredi kartı",
                            ObligationType.TemporaryPayment =>
                                "Geçici ödeme planı",
                            ObligationType.InstallmentPayment =>
                                "Taksit / finansman",
                            _ => "Planlı ödeme"
                        }));
            }

            HasUpcomingPayments = UpcomingPayments.Count > 0;
            HasNoUpcomingPayments = !HasUpcomingPayments;
            CalculationDetails = BuildDetails(current);
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
    private Task OpenSimulationAsync() =>
        Shell.Current.GoToAsync("//main/simulation/simulation-content");

    [RelayCommand]
    private Task OpenSettingsAsync() =>
        Shell.Current.GoToAsync("settings");

    private static string BuildDetails(SalaryPeriodProjection row)
    {
        var incomeLines = row.IncomeItems.Select(x =>
            $"{x.SourceDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}");
        var paymentLines = row.MandatoryItems.Select(x =>
            $"{x.DueDate:dd.MM} {x.Name}: {Money(x.Amount, 2)}" +
            (x.IsEstimate ? " (tahmini)" : string.Empty));
        return string.Join(
            Environment.NewLine,
            incomeLines
                .Concat(paymentLines)
                .Append($"Zorunlu toplam: {Money(row.MandatoryOutflow, 2)}")
                .Append($"Kullanılabilir alan: {Money(row.AvailableAfterMandatory, 2)}")
                .Append($"Tahmini yaşam: {Money(row.LivingBudget, 2)}")
                .Append($"Tahmini tasarruf: {Money(row.EstimatedSavingsCapacity, 2)}"));
    }

    private static string PeriodText(SalaryPeriod period) =>
        $"{period.Start.ToString("dd MMM", TurkishCulture)} → {period.End.ToString("dd MMM yyyy", TurkishCulture)}";
}
