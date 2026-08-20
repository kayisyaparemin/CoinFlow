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
    public ObservableCollection<UpcomingPaymentLine>
        PreFirstSalaryPayments { get; } = [];

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string assignmentModeText = "—";
    [ObservableProperty] private string paymentWindowText = "—";
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
    [ObservableProperty] private bool hasPreFirstSalaryPayments;
    [ObservableProperty] private bool hasUndeterminedCardPayment;
    [ObservableProperty] private string calculationDetails = string.Empty;
    [ObservableProperty] private string strategyStatusText = "—";
    [ObservableProperty] private string pendingStrategyText = string.Empty;
    [ObservableProperty] private bool hasPendingStrategy;

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

            CurrentPeriodText =
                $"{current.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";
            AssignmentModeText = current.PaymentAssignmentMode ==
                                 CoinFlow.Domain.Models.PaymentAssignmentMode.PreviousPeriod
                ? "Geçmiş dönemi kapatırım"
                : "Gelecek dönemi karşılarım";
            PaymentWindowText =
                $"{current.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
                $"{current.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} ödemeleri";
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
            StrategyStatusText = AssignmentModeText;
            HasPendingStrategy = dashboard.PendingStrategy is not null;
            PendingStrategyText = dashboard.PendingStrategy is null
                ? string.Empty
                : $"{dashboard.PendingStrategy.EffectiveFromSalaryDate.ToString("dd MMMM yyyy", TurkishCulture)} maaşından itibaren " +
                  ModeText(dashboard.PendingStrategy.Mode);

            PreFirstSalaryPayments.Clear();
            foreach (var payment in dashboard.PreFirstSalaryObligations)
            {
                PreFirstSalaryPayments.Add(ToLine(
                    payment,
                    "Sonraki maaştan önce vadesi geliyor"));
            }
            HasPreFirstSalaryPayments = PreFirstSalaryPayments.Count > 0;

            UpcomingPayments.Clear();
            foreach (var payment in dashboard.UpcomingPayments.Where(x =>
                         !x.IsPreFirstSalaryObligation))
            {
                var category = payment.IsEstimate
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
                    };
                var assignmentWarning = payment.PaymentBeforeSalary
                    ? $" • ⚠ {payment.AssignedSalaryDate.ToString("dd MMM", TurkishCulture)} maaşına atanıyor; gerçek vade {payment.DueDate.ToString("dd MMM", TurkishCulture)}"
                    : string.Empty;
                UpcomingPayments.Add(new UpcomingPaymentLine(
                    payment.DueDate.ToString("dd MMM", TurkishCulture),
                    payment.Name,
                    Money(payment.Amount),
                    category + assignmentWarning));
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
            (x.IsEstimate ? " (tahmini)" : string.Empty) +
            (x.PaymentBeforeSalary
                ? $" • ⚠ {x.AssignedSalaryDate:dd.MM} maaşı; gerçek vade önce"
                : string.Empty));
        return string.Join(
            Environment.NewLine,
            incomeLines
                .Concat(paymentLines)
                .Append($"Zorunlu toplam: {Money(row.MandatoryOutflow, 2)}")
                .Append($"Kullanılabilir alan: {Money(row.AvailableAfterMandatory, 2)}")
                .Append($"Tahmini yaşam: {Money(row.LivingBudget, 2)}")
                .Append($"Tahmini tasarruf: {Money(row.EstimatedSavingsCapacity, 2)}"));
    }

    private static UpcomingPaymentLine ToLine(
        ObligationItem payment,
        string detail) => new(
        payment.DueDate.ToString("dd MMM", TurkishCulture),
        payment.Name,
        Money(payment.Amount),
        detail);

    private static string ModeText(
        CoinFlow.Domain.Models.PaymentAssignmentMode mode) =>
        mode == CoinFlow.Domain.Models.PaymentAssignmentMode.PreviousPeriod
            ? "Geçmiş dönemi kapatırım"
            : "Gelecek dönemi karşılarım";

    private static string PeriodText(SalaryPeriod period) =>
        $"{period.Start.ToString("dd MMM", TurkishCulture)} → {period.End.ToString("dd MMM yyyy", TurkishCulture)}";
}
