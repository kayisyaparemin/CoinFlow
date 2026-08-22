using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Services;
using CoinFlow.App.Models;
using CoinFlow.App.Pages;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class DashboardViewModel(
    CoinFlowService service,
    IServiceProvider services) : ViewModelBase
{
    public ObservableCollection<UpcomingPaymentLine>
        UpcomingPayments
    { get; } = [];
    public ObservableCollection<UpcomingPaymentLine>
        PreFirstSalaryPayments
    { get; } = [];

    [ObservableProperty] private string currentPeriodText = "—";
    [ObservableProperty] private string currentSnapshotDate = "—";
    [ObservableProperty] private string planningStartingState = "—";
    [ObservableProperty] private string assignmentModeText = "—";
    [ObservableProperty] private string paymentWindowText = "—";
    [ObservableProperty] private string income = "—";
    [ObservableProperty] private string mandatory = "—";
    [ObservableProperty] private string available = "—";
    [ObservableProperty] private string carryOverDeficit = "—";
    [ObservableProperty] private string availableAfterCarryOverDeficit = "—";
    [ObservableProperty] private string carryOverMessage = string.Empty;
    [ObservableProperty] private bool hasCarryOverDeficit;
    [ObservableProperty] private string living = "—";
    [ObservableProperty] private string estimatedSavings = "—";
    [ObservableProperty] private string endingSavings = "—";
    [ObservableProperty] private string twelveMonthSavings = "—";
    [ObservableProperty] private string twelveMonthInterest = "—";
    [ObservableProperty] private string twelveMonthCardInterest = "—";
    [ObservableProperty] private string twelveMonthDeficitInterest = "—";
    [ObservableProperty] private bool hasTwelveMonthInterest;
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
    [ObservableProperty] private bool hasFinancialPlan;
    [ObservableProperty] private bool isEmptyState = true;
    [ObservableProperty]
    private string emptyStateMessage =
        "Başlamak için maaşını ekle.";
    [ObservableProperty] private string emptyStateAction = "Maaş Ekle";
    [ObservableProperty] private bool hasPendingReview;
    [ObservableProperty] private string pendingReviewTitle = string.Empty;
    [ObservableProperty] private string pendingReviewMessage = string.Empty;

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
            var review = await service.GetPeriodReviewAvailabilityAsync();
            HasPendingReview = review.IsDue;
            PendingReviewTitle = review.IsDue
                ? "Geçen dönemi güncelle"
                : string.Empty;
            PendingReviewMessage = review.IsDue
                ? "Planınla gerçekte olanı karşılaştır ve yeni planını güncel durumundan başlat."
                : string.Empty;
            var dashboard = await service.GetDashboardAsync();
            if (dashboard is null)
            {
                var plan = await service.GetFinancialPlanAsync();
                HasFinancialPlan = false;
                IsEmptyState = true;
                HasPreFirstSalaryPayments = false;
                HasUpcomingPayments = false;
                HasNoUpcomingPayments = true;
                HasUndeterminedCardPayment = false;
                HasPendingStrategy = false;
                HasTwelveMonthInterest = false;
                PreFirstSalaryPayments.Clear();
                UpcomingPayments.Clear();
                EmptyStateMessage = plan.Salaries.Count == 0
                    ? "Henüz finansal plan oluşturulmadı. Başlamak için maaşını ekle."
                    : "Maaş kullanım düzenini seçerek 12 aylık planı tamamla.";
                EmptyStateAction = plan.Salaries.Count == 0
                    ? "Maaş Ekle"
                    : "Düzeni Seç";
                return;
            }

            HasFinancialPlan = true;
            IsEmptyState = false;
            var current = dashboard.CurrentPeriod;

            CurrentSnapshotDate = dashboard.ProjectionAnchorDate == default
                ? "Henüz güncellenmedi"
                : dashboard.ProjectionAnchorDate.ToString(
                    "dd MMMM yyyy",
                    TurkishCulture);
            PlanningStartingState = Money(
                dashboard.ProjectionStartingSavings);
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
            CarryOverDeficit = Money(-current.CarryOverDeficit);
            AvailableAfterCarryOverDeficit = Money(
                current.AvailableAfterCarryOverDeficit);
            HasCarryOverDeficit = current.HasCarryOverDeficit;
            CarryOverMessage = current.HasCarryOverDeficit
                ? current.RemainingCarryOverDeficit > 0m
                    ? $"Bu dönem {Money(current.DeficitCoveredThisPeriod)} karşılanıyor; sonraki döneme {Money(current.RemainingCarryOverDeficit)} açık devrediyor."
                    : $"Devreden {Money(current.CarryOverDeficit)} açık bu dönem tamamen kapanıyor."
                : string.Empty;
            Living = Money(current.LivingBudget);
            EstimatedSavings = Money(current.EstimatedSavingsCapacity);
            EndingSavings = Money(current.EndingProjectedSavings);
            TwelveMonthSavings = Money(
                dashboard.TwelvePeriodEndingProjectedSavings);
            TwelveMonthInterest = Money(
                dashboard.TwelvePeriodTotalInterest);
            TwelveMonthCardInterest = Money(
                dashboard.TwelvePeriodCreditCardInterest);
            TwelveMonthDeficitInterest = Money(
                dashboard.TwelvePeriodDeficitFinancingInterest);
            HasTwelveMonthInterest =
                dashboard.TwelvePeriodTotalInterest > 0m;
            TightestPeriod = PeriodText(dashboard.TightestPeriod.Period);
            TightestValue = Money(
                dashboard.TightestPeriod.EndingProjectedSavings);
            HasDeficit = current.EndingProjectedSavings < 0m;
            DeficitMessage = HasDeficit
                ? $"Dönem sonu durumunda {Money(Math.Abs(current.EndingProjectedSavings))} finansman açığı oluşuyor."
                : string.Empty;
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
                    "Son ödeme tarihi maaştan önce"));
            }
            HasPreFirstSalaryPayments = PreFirstSalaryPayments.Count > 0;

            UpcomingPayments.Clear();
            foreach (var payment in dashboard.UpcomingPayments.Where(x =>
                         !x.IsPreFirstSalaryObligation))
            {
                var category = payment.IsEstimate
                    ? "Kart ödemesi • Tahmini"
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
                    ? $" • ⚠ Karşılayan maaş: {payment.AssignedSalaryDate.ToString("dd MMM", TurkishCulture)}; son ödeme: {payment.DueDate.ToString("dd MMM", TurkishCulture)}"
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
            SetStatus(UserFacingMessages.FromException(exception));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenSimulationAsync() =>
        Shell.Current.GoToAsync("//simulation/simulation-content");

    [RelayCommand]
    private Task OpenSettingsAsync() =>
        Shell.Current.GoToAsync("//settings/settings-content");

    [RelayCommand]
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    public Task OpenPeriodReviewAsync()
    {
        var page = services.GetRequiredService<PeriodReviewPage>();
        return Shell.Current.Navigation.PushModalAsync(
            new NavigationPage(page));
    }

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
        var calculation = new[]
        {
            $"OpeningProjectedSavings: {Money(row.OpeningProjectedSavings, 2)}",
            $"CarryOverDeficit: {Money(row.CarryOverDeficit, 2)}",
            $"Income: {Money(row.TotalIncome, 2)}",
            $"MandatoryOutflow: {Money(row.MandatoryOutflow, 2)}",
            $"AvailableAfterMandatory: {Money(row.AvailableAfterMandatory, 2)}",
            $"AvailableAfterCarryOverDeficit: {Money(row.AvailableAfterCarryOverDeficit, 2)}",
            $"LivingBudget: {Money(row.LivingBudget, 2)}",
            $"LargeExpenses: {Money(row.PlannedLargeCashExpenses, 2)}",
            $"CurrentPeriodNetContribution: {Money(row.CurrentPeriodNetContribution, 2)}",
            $"EndingBeforeDeficitInterest: {Money(row.EndingProjectedSavingsBeforeDeficitInterest, 2)}",
            $"DeficitPrincipal: {Money(row.DeficitPrincipal, 2)}",
            $"DeficitInterestRate: %{row.AppliedDeficitInterestRate * 100m:N2}",
            $"DeficitInterest: {Money(row.DeficitFinancingInterest, 2)}",
            $"CardInterestGenerated: {Money(row.CardInterestGenerated, 2)}",
            $"TotalInterestGenerated: {Money(row.TotalInterestGenerated, 2)}",
            $"FinalEndingProjectedSavings: {Money(row.EndingProjectedSavings, 2)}",
            string.Empty
        };
        var cardInterestLines = row.CardPaymentStatuses.Select(x =>
            $"{x.CardName}: Statement={Money(x.StatementBalance ?? 0m, 2)} • " +
            $"Payment={Money(x.Payment ?? 0m, 2)} • " +
            $"RemainingPrincipal={Money(x.CarriedPrincipalAfterPayment ?? 0m, 2)} • " +
            $"Rate=%{x.AppliedInterestRate * 100m:N2} • " +
            $"CarryInterest={Money(x.CarryInterest, 2)} • " +
            $"NextCarry={Money(x.NextCarriedBalance ?? 0m, 2)}");
        return string.Join(
            Environment.NewLine,
            calculation.Concat(incomeLines)
                .Concat(paymentLines)
                .Concat(cardInterestLines));
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
