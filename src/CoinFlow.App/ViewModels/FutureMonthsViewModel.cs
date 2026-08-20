using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class FutureMonthsViewModel(
    CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<ProjectionLine> Periods { get; } = [];

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
                var notice = Notice(row);
                Periods.Add(new ProjectionLine(
                    SalaryText(row),
                    AssignmentText(row),
                    Money(row.AvailableAfterMandatory),
                    Money(-row.CarryOverDeficit),
                    Money(row.AvailableAfterCarryOverDeficit),
                    row.HasCarryOverDeficit,
                    Money(row.LivingBudget),
                    Money(row.PlannedLargeCashExpenses),
                    row.PlannedLargeCashExpenses > 0m,
                    Money(row.EstimatedSavingsCapacity),
                    Money(row.EndingProjectedSavingsBeforeDeficitInterest),
                    Money(row.CardInterestGenerated),
                    Money(row.DeficitFinancingInterest),
                    Money(row.TotalInterestGenerated),
                    InterestLabel(row),
                    row.TotalInterestGenerated > 0m,
                    Money(row.EndingProjectedSavings),
                    CarryOverMessage(row),
                    Breakdown(row),
                    notice,
                    !string.IsNullOrWhiteSpace(notice)));
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

    private static string Breakdown(SalaryPeriodProjection row)
    {
        var lines = new List<string>
        {
            $"Gelir: {Money(row.TotalIncome)}",
            $"Zorunlu ödemeler: {Money(row.MandatoryOutflow)}",
            $"Zorunlular sonrası: {Money(row.AvailableAfterMandatory)}"
        };
        if (row.HasCarryOverDeficit)
        {
            lines.Add(
                $"Önceki dönemden devreden açık: {Money(row.CarryOverDeficit)}");
            lines.Add(
                $"Açık kapandıktan sonra kalan: {Money(row.AvailableAfterCarryOverDeficit)}");
        }
        else if (row.OpeningProjectedSavings > 0m)
        {
            lines.Add(
                $"Dönem başı tahmini birikim: {Money(row.OpeningProjectedSavings)}");
        }

        lines.AddRange(
        [
            $"Tahmini yaşam gideri: {Money(row.LivingBudget)}",
            $"Büyük planlı ödeme: {Money(row.PlannedLargeCashExpenses)}",
            $"Bu dönemin tahmini tasarrufu: {Money(row.EstimatedSavingsCapacity)}",
            $"Faiz öncesi dönem sonu: {Money(row.EndingProjectedSavingsBeforeDeficitInterest)}",
            $"Dönem sonu tahmini birikim: {Money(row.EndingProjectedSavings)}",
            string.Empty,
            $"Krediler: {Money(row.LoanPayments)}",
            $"Kartlar: {Money(row.CreditCardPayments)}",
            $"Geçici planlar: {Money(row.TemporaryPayments)}",
            $"Taksit / finansman: {Money(row.InstallmentPayments)}",
            $"Diğer planlı: {Money(row.OtherScheduledPayments)}",
            $"Normal zorunlu yük: {Money(row.NormalMandatoryAmount)}",
            $"Geçmiş düzenden kapanacak: {Money(row.TransitionCatchUpAmount)}",
            $"İleri dönem için ayrılacak: {Money(row.ForwardFundedAmount)}"
        ]);
        if (row.TotalInterestGenerated > 0m)
        {
            lines.Add(string.Empty);
            lines.Add("Faiz Maliyeti");
            lines.Add(
                $"Kredi kartı devreden borç: {Money(row.CardInterestGenerated)}");
            foreach (var card in row.CardPaymentStatuses.Where(x =>
                         x.CarryInterest > 0m))
            {
                lines.Add($"{card.CardName}: {Money(card.CarryInterest)}");
            }

            lines.Add(
                $"Finansman açığı: {Money(row.DeficitFinancingInterest)}");
            lines.Add(
                $"Bu dönem oluşan toplam: {Money(row.TotalInterestGenerated)}");
        }
        if (row.HasCarryOverDeficit)
        {
            lines.Add($"Bu dönem karşılanan açık: {Money(row.DeficitCoveredThisPeriod)}");
            lines.Add(row.RemainingCarryOverDeficit > 0m
                ? $"Sonraki döneme devreden açık: {Money(row.RemainingCarryOverDeficit)}"
                : $"Bu dönem açık tamamen kapandı. Açık sonrası kalan: {Money(row.EndingProjectedSavings)}");
        }

        var exactSources = row.MandatoryItems.Select(x =>
            $"{x.DueDate:dd.MM.yyyy} • {x.Name}: {Money(x.Amount, 2)}" +
            (x.IsEstimate ? " (tahmini)" : string.Empty) +
            AssignmentDetail(x));
        var undeterminedCards = row.CardPaymentStatuses
            .Where(x => x.Payment is null)
            .Select(x =>
                $"{x.PaymentDueDate:dd.MM.yyyy} • {x.CardName}: ödeme belirlenmedi" +
                (x.PaymentBeforeSalary
                    ? $" → {x.AssignedSalaryDate.ToString("dd MMM", TurkishCulture)} maaşı • ⚠ Maaştan önce vadesi geliyor"
                    : string.Empty));
        var debugLines = BuildInfo.IsDevelopment
            ? row.CardPaymentStatuses.Select(x => string.Join(
                    " • ",
                    $"{x.CardName} Statement={Money(x.StatementBalance ?? 0m, 2)}",
                    $"Payment={Money(x.Payment ?? 0m, 2)}",
                    $"RemainingPrincipal={Money(x.CarriedPrincipalAfterPayment ?? 0m, 2)}",
                    $"InterestRate=%{x.AppliedInterestRate * 100m:N2}",
                    $"CarryInterest={Money(x.CarryInterest, 2)}",
                    $"NextCarry={Money(x.NextCarriedBalance ?? 0m, 2)}"))
                .Concat([
                    $"OpeningSavings={Money(row.OpeningProjectedSavings, 2)}",
                    $"CurrentContribution={Money(row.CurrentPeriodNetContribution, 2)}",
                    $"EndingBeforeDeficitInterest={Money(row.EndingProjectedSavingsBeforeDeficitInterest, 2)}",
                    $"DeficitPrincipal={Money(row.DeficitPrincipal, 2)}",
                    $"DeficitInterestRate=%{row.AppliedDeficitInterestRate * 100m:N2}",
                    $"DeficitInterest={Money(row.DeficitFinancingInterest, 2)}",
                    $"FinalEndingSavings={Money(row.EndingProjectedSavings, 2)}"
                ])
            : [];
        return string.Join(
            Environment.NewLine,
            lines.Concat(exactSources)
                .Concat(undeterminedCards)
                .Concat(debugLines));
    }

    private static string CarryOverMessage(SalaryPeriodProjection row)
    {
        if (!row.HasCarryOverDeficit)
        {
            return string.Empty;
        }

        return row.RemainingCarryOverDeficit > 0m
            ? $"Bu dönem {Money(row.DeficitCoveredThisPeriod)} karşılanıyor; sonraki döneme {Money(row.RemainingCarryOverDeficit)} açık devrediyor."
            : $"{Money(row.CarryOverDeficit)} devreden açık bu dönem tamamen kapanıyor; dönem sonu {Money(row.EndingProjectedSavings)}.";
    }

    private static string Notice(SalaryPeriodProjection row)
    {
        var notices = new List<string>();
        if (row.HasUndeterminedCardPayment)
        {
            notices.Add("Kart ödemesi henüz belirlenmedi.");
        }

        if (row.IsEstimatedCardPayment)
        {
            notices.Add("Kart ödemesinde projeksiyon varsayımı kullanıldı.");
        }

        if (row.EstimatedSavingsCapacity < 0m)
        {
            notices.Add(
                $"Yaşam bütçesi ve büyük planlı ödemeler sonrası {Money(Math.Abs(row.EstimatedSavingsCapacity))} açık.");
        }

        if (row.TotalInterestGenerated > 0m)
        {
            notices.Add(
                $"Bu dönem tahmini faiz yükü {Money(row.TotalInterestGenerated)}.");
        }

        if (row.IsStrategyTransition)
        {
            notices.Add(
                $"Düzen değişikliği dönemi • ek geçiş yükü {Money(row.TransitionCatchUpAmount)}.");
        }

        if (row.IsInitialSnapshotPeriod)
        {
            notices.Add("Geçiş / snapshot sonrası ilk dönem.");
        }

        if (row.MandatoryItems.Any(x => x.PaymentBeforeSalary) ||
            row.CardPaymentStatuses.Any(x => x.PaymentBeforeSalary))
        {
            notices.Add("⚠ Bazı ödemeler atanmış maaştan önce vadesi geliyor.");
        }

        return string.Join(" ", notices);
    }

    private static string InterestLabel(SalaryPeriodProjection row) =>
        (row.CardInterestGenerated > 0m,
            row.DeficitFinancingInterest > 0m) switch
        {
            (true, false) => "Kart borcu faizi",
            (false, true) => "Açık faizi",
            _ => "Bu dönem toplam faiz"
        };

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

    private static string AssignmentDetail(ObligationItem item)
    {
        if (item.AssignedSalaryDate == default)
        {
            return string.Empty;
        }

        var assignment =
            $" → {item.AssignedSalaryDate.ToString("dd MMM", TurkishCulture)} maaşı";
        return item.PaymentBeforeSalary
            ? assignment + " • ⚠ Maaştan önce vadesi geliyor"
            : assignment;
    }
}
