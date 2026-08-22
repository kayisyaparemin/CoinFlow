using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class PeriodReviewWizardViewModel(
    CoinFlowService service,
    IUserFeedbackService feedback) : ViewModelBase
{
    private PeriodReviewContext? _context;
    private decimal _lastSuggestedSavings;
    private bool _loaded;

    public ObservableCollection<ActualPaymentInputItem> Payments { get; } = [];
    public ObservableCollection<ActualFlowInputItem> Flows { get; } = [];
    public ObservableCollection<ComparisonUiLine> ComparisonLines { get; } = [];

    [ObservableProperty] private int currentStep = 1;
    [ObservableProperty] private bool isSuccess;
    [ObservableProperty] private string periodText = string.Empty;
    [ObservableProperty] private string plannedIncome = string.Empty;
    [ObservableProperty] private string plannedLoans = string.Empty;
    [ObservableProperty] private string plannedCards = string.Empty;
    [ObservableProperty] private string plannedTemporary = string.Empty;
    [ObservableProperty] private string plannedInstallments = string.Empty;
    [ObservableProperty] private string plannedOther = string.Empty;
    [ObservableProperty] private string plannedLarge = string.Empty;
    [ObservableProperty] private string plannedLiving = string.Empty;
    [ObservableProperty] private string plannedInterest = string.Empty;
    [ObservableProperty] private string plannedEnding = string.Empty;
    [ObservableProperty] private bool hasPlanRevision;
    [ObservableProperty] private string planRevisionNotice = string.Empty;
    [ObservableProperty] private string actualLivingSpend = string.Empty;
    [ObservableProperty] private string actualInterest = "0";
    [ObservableProperty] private string currentStartingSavings = string.Empty;
    [ObservableProperty] private string suggestedStartingSavings = string.Empty;
    [ObservableProperty] private bool showLivingBreakdown;
    [ObservableProperty] private string groceryAmount = string.Empty;
    [ObservableProperty] private string fuelAmount = string.Empty;
    [ObservableProperty] private string diningAmount = string.Empty;
    [ObservableProperty] private string entertainmentAmount = string.Empty;
    [ObservableProperty] private string otherLivingAmount = string.Empty;
    [ObservableProperty] private string newPaymentName = string.Empty;
    [ObservableProperty] private string newPaymentCategory = "Diğer";
    [ObservableProperty] private string newPaymentAmount = string.Empty;
    [ObservableProperty] private DateTime newPaymentDate = DateTime.Today;
    [ObservableProperty] private string newIncomeName = string.Empty;
    [ObservableProperty] private string newIncomeCategory = "Ek gelir";
    [ObservableProperty] private string newIncomeAmount = string.Empty;
    [ObservableProperty] private DateTime newIncomeDate = DateTime.Today;
    [ObservableProperty] private string comparisonSummary = string.Empty;
    [ObservableProperty] private string comparisonPlannedEnding = string.Empty;
    [ObservableProperty] private string comparisonActualEnding = string.Empty;
    [ObservableProperty] private string comparisonDifference = string.Empty;
    [ObservableProperty] private string successText = string.Empty;

    public bool IsStep1 => CurrentStep == 1 && !IsSuccess;
    public bool IsStep2 => CurrentStep == 2 && !IsSuccess;
    public bool IsStep3 => CurrentStep == 3 && !IsSuccess;
    public bool IsReviewVisible => !IsSuccess;
    public bool HasPayments => Payments.Count > 0;
    public bool HasFlows => Flows.Count > 0;
    public bool IsNotBusy => !IsBusy;
    public string PlanIndicator => CurrentStep == 1 ? "●  Plan" : "✓  Plan";
    public string ActualIndicator => CurrentStep < 2
        ? "○  Gerçek"
        : CurrentStep == 2 ? "●  Gerçek" : "✓  Gerçek";
    public string ResultIndicator => CurrentStep < 3
        ? "○  Sonuç"
        : "●  Sonuç";

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            IsBusy = true;
            _context = await service.GetPeriodReviewContextAsync();
            if (_context.Actual is not null)
            {
                throw new InvalidOperationException(
                    "Bu dönem daha önce kaydedildi.");
            }

            var plan = _context.OriginalPlan;
            var finalPlan = FinalPlanValues.From(plan, _context.Revision);
            PeriodText =
                $"{plan.PeriodStart:dd MMMM yyyy} → {plan.PeriodEnd:dd MMMM yyyy}";
            PlannedIncome = Money(finalPlan.PlannedIncome, 2);
            PlannedLoans = Money(finalPlan.PlannedLoanPayments, 2);
            PlannedCards = Money(finalPlan.PlannedCardPayments, 2);
            PlannedTemporary = Money(finalPlan.PlannedTemporaryPayments, 2);
            PlannedInstallments = Money(
                finalPlan.PlannedInstallmentPayments,
                2);
            PlannedOther = Money(finalPlan.PlannedOtherScheduledPayments, 2);
            PlannedLarge = Money(finalPlan.PlannedLargeExpenses, 2);
            PlannedLiving = Money(finalPlan.PlannedLivingBudget, 2);
            PlannedInterest = Money(finalPlan.PlannedInterest, 2);
            PlannedEnding = Money(finalPlan.PlannedEndingSavings, 2);
            HasPlanRevision = _context.RevisionCount > 0;
            PlanRevisionNotice = HasPlanRevision
                ? $"Bu plan dönem içinde {_context.RevisionCount} kez güncellendi."
                : string.Empty;
            var plannedLivingText = finalPlan.PlannedLivingBudget.ToString(
                "0.##",
                TurkishCulture);
            ActualLivingSpend = plannedLivingText;
            ActualInterest = finalPlan.PlannedDeficitInterest.ToString(
                "0.##",
                TurkishCulture);
            _lastSuggestedSavings = _context.SuggestedStartingSavings;
            SuggestedStartingSavings = Money(_lastSuggestedSavings, 2);
            CurrentStartingSavings = _lastSuggestedSavings.ToString(
                "0.##",
                TurkishCulture);

            Payments.Clear();
            foreach (var line in FinalPaymentLines(plan, _context.Revision))
            {
                var item = new ActualPaymentInputItem
                {
                    PlanLineId = line.Id,
                    Name = line.Name,
                    PlannedDate = line.PlannedDate,
                    PlannedAmountValue = line.PlannedAmount,
                    ActualAmount = line.PlannedAmount?.ToString(
                        "0.##",
                        TurkishCulture) ?? string.Empty,
                    ActualDate = line.PlannedDate.ToDateTime(
                        TimeOnly.MinValue)
                };
                item.SelectedStatus = item.StatusOptions.First(x =>
                    x.Value == (line.PlannedAmount is null
                        ? ActualPaymentStatus.Unpaid
                        : ActualPaymentStatus.Paid));
                Payments.Add(item);
            }

            OnPropertyChanged(nameof(HasPayments));
            _loaded = true;
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
    private async Task NextAsync()
    {
        try
        {
            SetStatus(string.Empty);
            if (CurrentStep == 1)
            {
                CurrentStep = 2;
                return;
            }

            if (CurrentStep == 2)
            {
                await RefreshPreviewAsync(true);
                CurrentStep = 3;
            }
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1 && !IsBusy)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private async Task RecalculateSuggestedAsync()
    {
        try
        {
            await RefreshPreviewAsync(true);
            SetStatus("Önerilen yeni başlangıç durumu güncellendi.");
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    [RelayCommand]
    private void AddUnplannedPayment()
    {
        AddFlow(
            ActualFlowType.UnplannedPayment,
            NewPaymentName,
            NewPaymentCategory,
            NewPaymentDate,
            NewPaymentAmount);
        NewPaymentName = string.Empty;
        NewPaymentAmount = string.Empty;
    }

    [RelayCommand]
    private void AddUnplannedIncome()
    {
        AddFlow(
            ActualFlowType.UnplannedIncome,
            NewIncomeName,
            NewIncomeCategory,
            NewIncomeDate,
            NewIncomeAmount);
        NewIncomeName = string.Empty;
        NewIncomeAmount = string.Empty;
    }

    [RelayCommand]
    private void RemoveFlow(ActualFlowInputItem item)
    {
        Flows.Remove(item);
        OnPropertyChanged(nameof(HasFlows));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        PeriodReviewDraft draft;
        try
        {
            SetStatus(string.Empty);
            draft = BuildDraft(true);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
            return;
        }

        try
        {
            IsBusy = true;
            var result = await service.FinalizePeriodReviewAsync(draft);
            ComparisonSummary = result.Comparison.Summary;
            SuccessText =
                $"{result.NewSnapshot.SnapshotDate:dd MMMM yyyy} itibarıyla yeni 12 aylık planın güncellendi.";
            await feedback.ShowSuccessAsync("Dönem bilgileri kaydedildi.");
            IsSuccess = true;
        }
        catch (Exception exception)
        {
            var message = UserFacingMessages.FromException(exception);
            SetStatus(message);
            await feedback.ShowErrorAsync(message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshPreviewAsync(bool updateConfirmedWhenUnchanged)
    {
        var currentBefore = ParseMoney(
            CurrentStartingSavings,
            "Yeni planlama başlangıç durumu");
        var suggested = await service.PreviewPeriodReviewAsync(
            BuildDraft(false));
        if (updateConfirmedWhenUnchanged &&
            currentBefore == _lastSuggestedSavings)
        {
            CurrentStartingSavings = suggested.SuggestedStartingSavings
                .ToString("0.##", TurkishCulture);
        }

        _lastSuggestedSavings = suggested.SuggestedStartingSavings;
        SuggestedStartingSavings = Money(_lastSuggestedSavings, 2);
        var preview = await service.PreviewPeriodReviewAsync(
            BuildDraft(true));
        ComparisonSummary = preview.Comparison.Summary;
        ComparisonPlannedEnding = Money(
            preview.Comparison.PlannedEndingSavings,
            2);
        ComparisonActualEnding = Money(
            preview.Comparison.ActualEndingSavings,
            2);
        ComparisonDifference = SignedMoney(
            preview.Comparison.Difference);
        ComparisonLines.Clear();
        foreach (var line in preview.Comparison.Lines)
        {
            ComparisonLines.Add(new ComparisonUiLine(
                line.Category,
                Money(line.Planned, 2),
                Money(line.Actual, 2),
                SignedMoney(line.Difference)));
        }
    }

    private PeriodReviewDraft BuildDraft(bool includeConfirmedSavings)
    {
        var context = _context ?? throw new InvalidOperationException(
            "Dönem bilgisi henüz yüklenmedi.");
        var paymentDrafts = Payments.Select(item =>
        {
            var status = item.SelectedStatus?.Value
                ?? throw new InvalidOperationException(
                    $"{item.Name} için ödeme durumu seçilmelidir.");
            var amount = status == ActualPaymentStatus.Unpaid
                ? 0m
                : ParseMoney(item.ActualAmount, $"{item.Name} gerçek ödeme");
            return new ActualPaymentDraft(
                item.PlanLineId,
                status,
                amount,
                status == ActualPaymentStatus.Unpaid
                    ? null
                    : DateOnly.FromDateTime(item.ActualDate),
                item.Note);
        }).ToArray();
        var living = ParseMoney(
            ActualLivingSpend,
            "Toplam yaşam gideri");
        var interest = string.IsNullOrWhiteSpace(ActualInterest)
            ? 0m
            : ParseMoney(ActualInterest, "Gerçekleşen faiz");
        var breakdown = new[]
        {
            Breakdown("Market", GroceryAmount),
            Breakdown("Yakıt", FuelAmount),
            Breakdown("Yeme-İçme", DiningAmount),
            Breakdown("Eğlence", EntertainmentAmount),
            Breakdown("Diğer", OtherLivingAmount)
        }.Where(x => x.Amount > 0m).ToArray();
        decimal? confirmed = includeConfirmedSavings
            ? ParseMoney(
                CurrentStartingSavings,
                "Yeni planlama başlangıç durumu")
            : null;
        return new PeriodReviewDraft(
            context.OriginalPlan.Id,
            paymentDrafts,
            living,
            interest,
            Flows.Select(x => new ActualFlowDraft(
                x.Type,
                x.Name,
                x.Category,
                x.Date,
                x.Amount)).ToArray(),
            breakdown,
            confirmed,
            string.Empty);
    }

    private LivingBreakdownDraft Breakdown(string category, string amount) =>
        new(
            category,
            string.IsNullOrWhiteSpace(amount)
                ? 0m
                : ParseMoney(amount, category));

    private void AddFlow(
        ActualFlowType type,
        string name,
        string category,
        DateTime date,
        string amountText)
    {
        try
        {
            var amount = ParseMoney(
                amountText,
                type == ActualFlowType.UnplannedIncome
                    ? "Gelir tutarı"
                    : "Ödeme tutarı");
            if (string.IsNullOrWhiteSpace(name) || amount <= 0m)
            {
                throw new InvalidOperationException(
                    "Ad ve sıfırdan büyük tutar gereklidir.");
            }

            Flows.Add(new ActualFlowInputItem(
                Guid.NewGuid(),
                type,
                name.Trim(),
                category.Trim(),
                DateOnly.FromDateTime(date),
                amount));
            OnPropertyChanged(nameof(HasFlows));
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(UserFacingMessages.FromException(exception));
        }
    }

    partial void OnCurrentStepChanged(int value) => NotifyStepProperties();
    partial void OnIsSuccessChanged(bool value) => NotifyStepProperties();
    private void NotifyStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsReviewVisible));
        OnPropertyChanged(nameof(PlanIndicator));
        OnPropertyChanged(nameof(ActualIndicator));
        OnPropertyChanged(nameof(ResultIndicator));
    }

    private static string SignedMoney(decimal value) =>
        $"{(value > 0m ? "+" : string.Empty)}{value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";

    private static IReadOnlyList<PeriodPlanPaymentLine> FinalPaymentLines(
        PeriodPlanSnapshot plan,
        PeriodPlanRevision? revision) =>
        revision?.PaymentLines.Count > 0
            ? revision.PaymentLines
            : plan.PaymentLines;

    private sealed record FinalPlanValues(
        decimal PlannedIncome,
        decimal PlannedLoanPayments,
        decimal PlannedCardPayments,
        decimal PlannedTemporaryPayments,
        decimal PlannedInstallmentPayments,
        decimal PlannedOtherScheduledPayments,
        decimal PlannedLargeExpenses,
        decimal PlannedLivingBudget,
        decimal PlannedInterest,
        decimal PlannedDeficitInterest,
        decimal PlannedEndingSavings)
    {
        public static FinalPlanValues From(
            PeriodPlanSnapshot plan,
            PeriodPlanRevision? revision) => revision is null
            ? new FinalPlanValues(
                plan.PlannedIncome,
                plan.PlannedLoanPayments,
                plan.PlannedCardPayments,
                plan.PlannedTemporaryPayments,
                plan.PlannedInstallmentPayments,
                plan.PlannedOtherScheduledPayments,
                plan.PlannedLargeExpenses,
                plan.PlannedLivingBudget,
                plan.PlannedCardInterest + plan.PlannedDeficitInterest,
                plan.PlannedDeficitInterest,
                plan.PlannedEndingSavings)
            : new FinalPlanValues(
                revision.PlannedIncome,
                revision.PlannedLoanPayments,
                revision.PlannedCardPayments,
                revision.PlannedTemporaryPayments,
                revision.PlannedInstallmentPayments,
                revision.PlannedOtherScheduledPayments,
                revision.PlannedLargeExpenses,
                revision.PlannedLivingBudget,
                revision.PlannedCardInterest +
                revision.PlannedDeficitInterest,
                revision.PlannedDeficitInterest,
                revision.PlannedEndingSavings);
    }
}
