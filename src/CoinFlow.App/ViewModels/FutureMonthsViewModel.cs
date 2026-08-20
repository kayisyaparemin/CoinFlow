using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class FutureMonthsViewModel(
    CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<ProjectionLine> Periods { get; } = [];
    private bool _preserveOnNextAppearance;

    [ObservableProperty] private string targetAmount = string.Empty;
    [ObservableProperty] private string targetResult = string.Empty;
    [ObservableProperty] private bool hasTargetResult;
    [ObservableProperty] private bool hasProjection;
    [ObservableProperty] private bool hasNoProjection = true;
    [ObservableProperty] private string emptyStateMessage =
        "Projeksiyon oluşturmak için önce maaş bilgisi ekle.";
    [ObservableProperty] private string totalCreditCardInterest = "—";
    [ObservableProperty] private string totalDeficitInterest = "—";
    [ObservableProperty] private string totalInterestCost = "—";
    [ObservableProperty] private bool hasInterestSummary;

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
            var rows = await service.GetFuturePeriodsAsync(
                periodCount: 12);
            var plan = await service.GetFinancialPlanAsync();
            Periods.Clear();
            foreach (var row in rows)
            {
                var beforeSalaryCount = row.MandatoryItems.Count(x =>
                                            x.PaymentBeforeSalary) +
                                        row.CardPaymentStatuses.Count(x =>
                                            x.Payment is null &&
                                            x.PaymentBeforeSalary);
                Periods.Add(new ProjectionLine(
                    row,
                    SalaryText(row),
                    AssignmentText(row),
                    Money(row.AvailableAfterMandatory),
                    Money(-row.CarryOverDeficit),
                    row.HasCarryOverDeficit,
                    Money(row.EstimatedSavingsCapacity),
                    Money(row.TotalInterestGenerated),
                    row.TotalInterestGenerated > 0m,
                    Money(row.EndingProjectedSavings),
                    beforeSalaryCount == 0
                        ? string.Empty
                        : $"Maaştan önce vadesi gelen {beforeSalaryCount} ödeme",
                    beforeSalaryCount > 0,
                    row.IsEstimatedCardPayment,
                    row.HasUndeterminedCardPayment));
            }

            HasProjection = Periods.Count > 0;
            HasNoProjection = !HasProjection;
            var interest = ProjectionInterestSummary.From(rows);
            TotalCreditCardInterest = Money(interest.CreditCardInterest);
            TotalDeficitInterest = Money(
                interest.DeficitFinancingInterest);
            TotalInterestCost = Money(interest.TotalInterestCost);
            HasInterestSummary = interest.TotalInterestCost > 0m;
            HasTargetResult = false;
            EmptyStateMessage = plan.Salaries.Count == 0
                ? "Projeksiyon oluşturmak için önce maaş bilgisi ekle."
                : "Projeksiyon için maaş kullanım düzenini seç.";
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
    private Task OpenCommitmentsAsync() =>
        Shell.Current.GoToAsync("//commitments/commitments-content");

    [RelayCommand]
    private async Task OpenPeriodDetailAsync(ProjectionLine? line)
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
                    new SalaryPeriodDetailRequest(line.Projection)
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

    [RelayCommand]
    private async Task FindTargetAsync()
    {
        try
        {
            var target = ParseMoney(TargetAmount, "Hedef tutar");
            var reached = await service.FindTargetPeriodAsync(target);
            TargetResult = reached is null
                ? $"{Money(target)} seviyesine gösterilen 12 maaş döneminde henüz ulaşılamıyor."
                : $"{Money(target)} tahmini birikim seviyesine {PeriodText(reached.Period)} döneminde ulaşıyorsun.";
            HasTargetResult = true;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            HasTargetResult = false;
            SetStatus(exception.Message);
        }
    }

    private static string PeriodText(SalaryPeriod period) =>
        $"{period.Start.ToString("dd MMM", TurkishCulture)} → {period.End.ToString("dd MMM yyyy", TurkishCulture)}";

    private static string SalaryText(SalaryPeriodProjection row) =>
        $"{row.PeriodStart.ToString("dd MMMM yyyy", TurkishCulture)} Maaşı";

    private static string AssignmentText(SalaryPeriodProjection row)
    {
        if (row.IsStrategyTransition)
        {
            return $"Düzen değişikliği dönemi • " +
                   $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
                   $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)}";
        }

        var action = row.PaymentAssignmentMode ==
                     CoinFlow.Domain.Models.PaymentAssignmentMode.PreviousPeriod
            ? "ödemelerini kapatır"
            : "ödemelerini karşılar";
        return $"{row.PaymentWindowStart.ToString("dd MMM", TurkishCulture)}–" +
               $"{row.PaymentWindowEnd.ToString("dd MMM", TurkishCulture)} {action}";
    }

}
