using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel(
    CoinFlowService service,
    CreditCardStatementCalculator cardCalculator) : ViewModelBase
{
    public event Action<InitialPaymentStrategySetup>?
        InitialStrategySetupRequested;
    public ObservableCollection<SelectionOption<string>> RecordTypes { get; } = [];

    public ObservableCollection<SelectionOption<CreditCardPaymentStrategy>>
        PaymentStrategies { get; } =
    [
        new("Her ekstrede bana sor", CreditCardPaymentStrategy.AskEachStatement),
        new("Sürekli asgari", CreditCardPaymentStrategy.Minimum),
        new("Ekstre tamamını öde", CreditCardPaymentStrategy.FullStatement),
        new("Sabit tutar öde", CreditCardPaymentStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<ProjectionFallbackStrategy>>
        ProjectionFallbackStrategies { get; } =
    [
        new("Tahmin yapma", ProjectionFallbackStrategy.None),
        new("Asgari varsay", ProjectionFallbackStrategy.Minimum),
        new("Tam ödeme varsay", ProjectionFallbackStrategy.FullStatement),
        new("Sabit tutar varsay", ProjectionFallbackStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<CreditCardPaymentType>>
        PaymentPlanTypes { get; } =
    [
        new("Asgari", CreditCardPaymentType.Minimum),
        new("Ekstre tamamı", CreditCardPaymentType.FullStatement),
        new("Özel tutar", CreditCardPaymentType.FixedAmount)
    ];

    public ObservableCollection<FinancialRecordLine> Items { get; } = [];
    public ObservableCollection<DatedAmountLine> PlanInstallments { get; } = [];
    public ObservableCollection<DatedAmountLine> CardFutureCharges { get; } = [];
    public ObservableCollection<CardPaymentPlanLine> CardPaymentPlans { get; } = [];

    private readonly List<FinancialRecordLine> _allItems = [];
    private readonly Dictionary<Guid, string> _cardChargeDescriptions = [];
    private Guid? _editingCardId;
    private DateOnly? _editingCardBalanceDate;

    [ObservableProperty] private bool isIncomeSection = true;
    [ObservableProperty] private bool isPaymentSection;
    [ObservableProperty] private SelectionOption<string>? selectedRecordType;
    [ObservableProperty] private bool isSalary;
    [ObservableProperty] private bool isOtherIncome;
    [ObservableProperty] private bool isLoan;
    [ObservableProperty] private bool isPlan;
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool isLargeExpense;
    [ObservableProperty] private bool hasNoSalary;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string bank = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;
    [ObservableProperty] private string note = string.Empty;

    [ObservableProperty] private string paymentDay = "10";
    [ObservableProperty] private DateTime nextPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string installmentCount = "12";
    [ObservableProperty] private string remainingDebt = string.Empty;
    [ObservableProperty] private string earlyClosureAmount = string.Empty;

    [ObservableProperty] private DateTime planPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string planPaymentAmount = string.Empty;

    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private string carriedBalance = string.Empty;
    [ObservableProperty] private string unbilledSpending = string.Empty;
    [ObservableProperty] private DateTime cardBalanceDate = DateTime.Today;
    [ObservableProperty] private string closingDay = "25";
    [ObservableProperty] private string dueDay = "5";
    [ObservableProperty] private string minimumRate = "40";
    [ObservableProperty] private DateTime cardChargeDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string cardChargeAmount = string.Empty;
    [ObservableProperty] private SelectionOption<CreditCardPaymentStrategy>? selectedPaymentStrategy;
    [ObservableProperty] private string fixedPaymentAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentStrategy;
    [ObservableProperty] private SelectionOption<ProjectionFallbackStrategy>? selectedProjectionFallbackStrategy;
    [ObservableProperty] private string projectionFallbackFixedAmount = string.Empty;
    [ObservableProperty] private bool isFixedProjectionFallback;
    [ObservableProperty] private DateTime cardPaymentPlanDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedPaymentPlanType;
    [ObservableProperty] private string cardPaymentPlanAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentPlan;
    [ObservableProperty] private bool isEditingCard;
    [ObservableProperty] private string saveButtonText = "Kaydet";

    public async Task LoadAsync()
    {
        var plan = await service.GetFinancialPlanAsync();
        HasNoSalary = plan.Salaries.Count == 0;
        _allItems.Clear();

        foreach (var salary in plan.Salaries.OrderByDescending(x => x.EffectiveDate))
        {
            _allItems.Add(new FinancialRecordLine(
                salary.Id,
                ManagementSection.Income,
                FinancialRecordKind.Salary,
                salary.Description.Length == 0 ? "Maaş" : salary.Description,
                $"Geçerli: {salary.EffectiveDate:dd.MM.yyyy}",
                Money(salary.Amount),
                salary.EffectiveDate > DateOnly.FromDateTime(DateTime.Today)
                    ? "Planlanan maaş"
                    : "Maaş"));
        }

        var initialSetup = await service
            .GetInitialPaymentStrategySetupAsync();
        if (initialSetup is not null)
        {
            InitialStrategySetupRequested?.Invoke(initialSetup);
        }

        foreach (var income in plan.OtherIncomes.OrderBy(x => x.ExactDate))
        {
            _allItems.Add(new FinancialRecordLine(
                income.Id,
                ManagementSection.Income,
                FinancialRecordKind.OtherIncome,
                income.Description.Length == 0 ? "Diğer gelir" : income.Description,
                income.ExactDate.ToString("dd.MM.yyyy"),
                Money(income.Amount),
                "Tek seferlik gelir"));
        }

        foreach (var loan in plan.Loans)
        {
            _allItems.Add(new FinancialRecordLine(
                loan.Id,
                ManagementSection.Payment,
                FinancialRecordKind.Loan,
                $"{loan.Bank} {loan.Name}".Trim(),
                $"Sonraki: {loan.NextPaymentDate:dd.MM.yyyy} • {loan.RemainingInstallmentCount} ödeme",
                Money(loan.MonthlyPayment),
                loan.RemainingDebt is decimal debt
                    ? $"Kalan borç: {Money(debt)}"
                    : "Kredi"));
        }

        foreach (var paymentPlan in plan.PaymentPlans)
        {
            _allItems.Add(new FinancialRecordLine(
                paymentPlan.Id,
                ManagementSection.Payment,
                paymentPlan.Kind == PaymentPlanKind.Temporary
                    ? FinancialRecordKind.TemporaryPlan
                    : FinancialRecordKind.InstallmentPlan,
                paymentPlan.Name,
                $"{paymentPlan.Installments.Count(x => !x.IsPaid)} ödeme • exact tarihli",
                Money(paymentPlan.Installments.Where(x => !x.IsPaid).Sum(x => x.Amount)),
                paymentPlan.Kind switch
                {
                    PaymentPlanKind.Temporary => "Geçici plan",
                    PaymentPlanKind.Installment => "Taksit / finansman",
                    PaymentPlanKind.Recurring => "Dönemsel ödeme",
                    _ => "Planlı ödeme"
                }));
        }

        foreach (var card in plan.CreditCards)
        {
            var upcoming = cardCalculator.Project(
                card,
                1,
                useProjectionFallback: true)[0];
            var paymentText = upcoming.Payment is decimal payment
                ? $"Yaklaşan tahmini ödeme: {Money(payment)} • {upcoming.PaymentDueDate:dd.MM.yyyy}"
                : "Yaklaşan ödeme henüz belirlenmedi";
            _allItems.Add(new FinancialRecordLine(
                card.Id,
                ManagementSection.Payment,
                FinancialRecordKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                paymentText,
                Money(card.KnownTotalDebt),
                $"Ödeme: {StrategyLabel(card.PaymentStrategy)} • Projeksiyon: {FallbackLabel(card.ProjectionFallbackStrategy)}"));
        }

        foreach (var expense in plan.PlannedLargeExpenses)
        {
            _allItems.Add(new FinancialRecordLine(
                expense.Id,
                ManagementSection.Payment,
                FinancialRecordKind.LargeExpense,
                expense.Name,
                $"{expense.ExactDate:dd.MM.yyyy} • {expense.Note}",
                Money(expense.Amount),
                "Büyük planlı ödeme"));
        }

        RefreshRecordTypes();
        RefreshVisibleItems();
        SelectedPaymentStrategy ??= PaymentStrategies[0];
        SelectedProjectionFallbackStrategy ??=
            ProjectionFallbackStrategies[0];
        SelectedPaymentPlanType ??= PaymentPlanTypes[0];
    }

    public async Task<bool> CompleteInitialStrategySetupAsync(
        PaymentAssignmentMode mode)
    {
        try
        {
            await service.CompleteInitialPaymentStrategySetupAsync(mode);
            SetStatus("Maaş kullanım düzeni kaydedildi; projeksiyon hazır.");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    [RelayCommand]
    private void ShowIncome()
    {
        IsIncomeSection = true;
        IsPaymentSection = false;
        CancelEditingCard();
        RefreshRecordTypes();
        RefreshVisibleItems();
    }

    [RelayCommand]
    private void ShowPayments()
    {
        IsIncomeSection = false;
        IsPaymentSection = true;
        CancelEditingCard();
        RefreshRecordTypes();
        RefreshVisibleItems();
    }

    partial void OnSelectedRecordTypeChanged(
        SelectionOption<string>? value)
    {
        IsSalary = value?.Value == "salary";
        IsOtherIncome = value?.Value == "income";
        IsLoan = value?.Value == "loan";
        IsPlan = value?.Value is "temporary" or "installment";
        IsCard = value?.Value == "card";
        IsLargeExpense = value?.Value == "large";
    }

    partial void OnSelectedPaymentStrategyChanged(
        SelectionOption<CreditCardPaymentStrategy>? value) =>
        IsFixedPaymentStrategy =
            value?.Value == CreditCardPaymentStrategy.FixedAmount;

    partial void OnSelectedProjectionFallbackStrategyChanged(
        SelectionOption<ProjectionFallbackStrategy>? value) =>
        IsFixedProjectionFallback =
            value?.Value == ProjectionFallbackStrategy.FixedAmount;

    partial void OnSelectedPaymentPlanTypeChanged(
        SelectionOption<CreditCardPaymentType>? value) =>
        IsFixedPaymentPlan =
            value?.Value == CreditCardPaymentType.FixedAmount;

    [RelayCommand]
    private void AddPlanPayment()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(PlanPaymentAmount, "Ödeme tutarı"),
                "Ödeme tutarı");
            PlanInstallments.Add(new DatedAmountLine(
                Guid.NewGuid(),
                DateOnly.FromDateTime(PlanPaymentDate),
                parsed));
            PlanPaymentAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private void AddCardCharge()
    {
        try
        {
            var parsed = RequirePositive(
                ParseMoney(CardChargeAmount, "Kart charge tutarı"),
                "Kart charge tutarı");
            var id = Guid.NewGuid();
            CardFutureCharges.Add(new DatedAmountLine(
                id,
                DateOnly.FromDateTime(CardChargeDate),
                parsed));
            _cardChargeDescriptions[id] = string.IsNullOrWhiteSpace(Note)
                ? "Gelecek taksit"
                : Note.Trim();
            CardChargeAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private void AddCardPaymentPlan()
    {
        try
        {
            var type = SelectedPaymentPlanType?.Value
                ?? throw new InvalidOperationException("Ödeme şekli seçilmelidir.");
            var parsed = type == CreditCardPaymentType.FixedAmount
                ? RequirePositive(
                    ParseMoney(CardPaymentPlanAmount, "Özel ödeme tutarı"),
                    "Özel ödeme tutarı")
                : (decimal?)null;
            var date = DateOnly.FromDateTime(CardPaymentPlanDate);
            var existing = CardPaymentPlans.FirstOrDefault(x => x.DueDate == date);
            if (existing is not null)
            {
                CardPaymentPlans.Remove(existing);
            }

            CardPaymentPlans.Add(new CardPaymentPlanLine(
                existing?.Id ?? Guid.NewGuid(),
                date,
                type,
                parsed));
            CardPaymentPlanAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public void RemovePlanPayment(DatedAmountLine line) =>
        PlanInstallments.Remove(line);

    public void RemoveCardCharge(DatedAmountLine line)
    {
        CardFutureCharges.Remove(line);
        _cardChargeDescriptions.Remove(line.Id);
    }

    public void RemoveCardPaymentPlan(CardPaymentPlanLine line) =>
        CardPaymentPlans.Remove(line);

    public async Task EditCardAsync(Guid cardId)
    {
        var card = (await service.GetFinancialPlanAsync()).CreditCards
            .Single(x => x.Id == cardId);
        _editingCardId = card.Id;
        _editingCardBalanceDate = card.BalanceAsOfDate;
        IsIncomeSection = false;
        IsPaymentSection = true;
        RefreshRecordTypes();
        SelectedRecordType = RecordTypes.Single(x => x.Value == "card");
        IsEditingCard = true;
        SaveButtonText = "Kartı güncelle";
        Name = card.Name;
        Bank = card.Bank;
        CardLimit = card.Limit.ToString("N2", TurkishCulture);
        CarriedBalance = card.CarriedBalance.ToString("N2", TurkishCulture);
        UnbilledSpending = card.UnbilledSpending.ToString("N2", TurkishCulture);
        CardBalanceDate = card.BalanceAsOfDate.ToDateTime(TimeOnly.MinValue);
        ClosingDay = card.StatementClosingDay.ToString(TurkishCulture);
        DueDay = card.PaymentDueDay.ToString(TurkishCulture);
        MinimumRate = (card.MinimumPaymentRate * 100m).ToString("N2", TurkishCulture);
        SelectedPaymentStrategy = PaymentStrategies.Single(x =>
            x.Value == card.PaymentStrategy);
        FixedPaymentAmount = card.FixedPaymentAmount?.ToString("N2", TurkishCulture) ?? string.Empty;
        SelectedProjectionFallbackStrategy =
            ProjectionFallbackStrategies.Single(x =>
                x.Value == card.ProjectionFallbackStrategy);
        ProjectionFallbackFixedAmount =
            card.ProjectionFallbackFixedAmount?.ToString("N2", TurkishCulture) ?? string.Empty;

        CardFutureCharges.Clear();
        _cardChargeDescriptions.Clear();
        foreach (var charge in card.Charges)
        {
            CardFutureCharges.Add(new DatedAmountLine(
                charge.Id,
                charge.PostingDate,
                charge.Amount));
            _cardChargeDescriptions[charge.Id] = charge.Description;
        }

        CardPaymentPlans.Clear();
        foreach (var payment in card.PaymentPlans)
        {
            CardPaymentPlans.Add(new CardPaymentPlanLine(
                payment.Id,
                payment.DueDate,
                payment.PaymentType,
                payment.Amount));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            switch (SelectedRecordType?.Value)
            {
                case "salary":
                    await service.SaveSalaryAsync(new SalaryScheduleEntry
                    {
                        Amount = RequirePositive(ParseMoney(Amount, "Maaş"), "Maaş"),
                        EffectiveDate = DateOnly.FromDateTime(EffectiveDate),
                        Description = string.IsNullOrWhiteSpace(Name) ? "Maaş" : Name.Trim()
                    });
                    break;
                case "income":
                    await service.SaveOtherIncomeAsync(new OneTimeIncome
                    {
                        Amount = RequirePositive(ParseMoney(Amount, "Gelir"), "Gelir"),
                        ExactDate = DateOnly.FromDateTime(EffectiveDate),
                        Description = string.IsNullOrWhiteSpace(Name) ? "Diğer gelir" : Name.Trim()
                    });
                    break;
                case "loan":
                    await SaveLoanAsync();
                    break;
                case "temporary":
                case "installment":
                    await SavePlanAsync();
                    break;
                case "card":
                    await SaveCardAsync();
                    break;
                case "large":
                    await service.SavePlannedLargeExpenseAsync(new PlannedLargeExpense
                    {
                        Name = RequireName(),
                        Amount = RequirePositive(ParseMoney(Amount, "Tutar"), "Tutar"),
                        ExactDate = DateOnly.FromDateTime(EffectiveDate),
                        Note = Note.Trim()
                    });
                    break;
                default:
                    throw new InvalidOperationException("Kayıt türü seçilmelidir.");
            }

            SetStatus("Kayıt kaydedildi.");
            ResetForm();
            await LoadAsync();
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public async Task DeleteAsync(FinancialRecordLine item)
    {
        switch (item.Kind)
        {
            case FinancialRecordKind.Salary:
                await service.DeleteSalaryAsync(item.Id);
                break;
            case FinancialRecordKind.OtherIncome:
                await service.DeleteOtherIncomeAsync(item.Id);
                break;
            case FinancialRecordKind.Loan:
                await service.DeleteLoanAsync(item.Id);
                break;
            case FinancialRecordKind.CreditCard:
                await service.DeleteCreditCardAsync(item.Id);
                break;
            case FinancialRecordKind.TemporaryPlan:
            case FinancialRecordKind.InstallmentPlan:
                await service.DeletePaymentPlanAsync(item.Id);
                break;
            case FinancialRecordKind.LargeExpense:
                await service.DeletePlannedLargeExpenseAsync(item.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item.Kind));
        }

        await LoadAsync();
    }

    [RelayCommand]
    private void CancelEditingCard()
    {
        _editingCardId = null;
        _editingCardBalanceDate = null;
        IsEditingCard = false;
        SaveButtonText = "Kaydet";
        CardFutureCharges.Clear();
        CardPaymentPlans.Clear();
        _cardChargeDescriptions.Clear();
    }

    private async Task SaveLoanAsync()
    {
        if (!int.TryParse(PaymentDay, out var day))
        {
            throw new InvalidOperationException("Ödeme günü geçerli olmalıdır.");
        }

        if (!int.TryParse(InstallmentCount, out var count))
        {
            throw new InvalidOperationException("Kalan taksit sayısı geçerli olmalıdır.");
        }

        await service.SaveLoanAsync(new Loan
        {
            Name = RequireName(),
            Bank = Bank.Trim(),
            MonthlyPayment = RequirePositive(ParseMoney(Amount, "Aylık ödeme"), "Aylık ödeme"),
            PaymentDay = day,
            NextPaymentDate = DateOnly.FromDateTime(NextPaymentDate),
            RemainingInstallmentCount = count,
            RemainingDebt = ParseOptionalMoney(RemainingDebt),
            EarlyClosureAmount = ParseOptionalMoney(EarlyClosureAmount)
        });
    }

    private async Task SavePlanAsync()
    {
        if (PlanInstallments.Count == 0)
        {
            throw new InvalidOperationException("En az bir exact ödeme ekleyin.");
        }

        var id = Guid.NewGuid();
        await service.SavePaymentPlanAsync(new TemporaryPaymentPlan
        {
            Id = id,
            Name = RequireName(),
            Kind = SelectedRecordType?.Value == "temporary"
                ? PaymentPlanKind.Temporary
                : PaymentPlanKind.Installment,
            Installments = PlanInstallments
                .OrderBy(x => x.Date)
                .Select(x => new TemporaryPaymentInstallment
                {
                    Id = x.Id,
                    PlanId = id,
                    DueDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray()
        });
    }

    private async Task SaveCardAsync()
    {
        if (!int.TryParse(ClosingDay, out var closeDay) ||
            !int.TryParse(DueDay, out var paymentDueDay))
        {
            throw new InvalidOperationException("Kart günleri geçerli olmalıdır.");
        }

        var minimumRatePercent = ParseMoney(MinimumRate, "Asgari oran");
        var strategy = SelectedPaymentStrategy?.Value
            ?? CreditCardPaymentStrategy.AskEachStatement;
        var fallback = SelectedProjectionFallbackStrategy?.Value
            ?? ProjectionFallbackStrategy.None;
        var cardId = _editingCardId ?? Guid.NewGuid();
        var card = new CreditCard
        {
            Id = cardId,
            Name = RequireName(),
            Bank = Bank.Trim(),
            Limit = RequirePositive(ParseMoney(CardLimit, "Kart limiti"), "Kart limiti"),
            CarriedBalance = Math.Max(0m, ParseMoney(CarriedBalance, "Devreden bakiye")),
            UnbilledSpending = Math.Max(0m, ParseMoney(UnbilledSpending, "Ekstreleşmemiş harcama")),
            BalanceAsOfDate = _editingCardBalanceDate ??
                DateOnly.FromDateTime(CardBalanceDate),
            StatementClosingDay = closeDay,
            PaymentDueDay = paymentDueDay,
            MinimumPaymentRate = minimumRatePercent / 100m,
            PaymentStrategy = strategy,
            FixedPaymentAmount = strategy == CreditCardPaymentStrategy.FixedAmount
                ? RequirePositive(ParseMoney(FixedPaymentAmount, "Sabit ödeme"), "Sabit ödeme")
                : null,
            ProjectionFallbackStrategy = fallback,
            ProjectionFallbackFixedAmount =
                fallback == ProjectionFallbackStrategy.FixedAmount
                    ? RequirePositive(ParseMoney(
                        ProjectionFallbackFixedAmount,
                        "Projeksiyon sabit tutarı"),
                        "Projeksiyon sabit tutarı")
                    : null,
            Charges = CardFutureCharges
                .OrderBy(x => x.Date)
                .Select(x => new CardCharge
                {
                    Id = x.Id,
                    CreditCardId = cardId,
                    Description = _cardChargeDescriptions.GetValueOrDefault(x.Id, "Gelecek taksit"),
                    PostingDate = x.Date,
                    Amount = x.Amount
                })
                .ToArray(),
            PaymentPlans = CardPaymentPlans
                .OrderBy(x => x.DueDate)
                .Select(x => new CreditCardPaymentPlan
                {
                    Id = x.Id,
                    CreditCardId = cardId,
                    DueDate = x.DueDate,
                    PaymentType = x.PaymentType,
                    Amount = x.Amount
                })
                .ToArray()
        };
        await service.SaveCreditCardAsync(card);
    }

    private void RefreshRecordTypes()
    {
        var selected = SelectedRecordType?.Value;
        RecordTypes.Clear();
        if (IsIncomeSection)
        {
            RecordTypes.Add(new SelectionOption<string>("Maaş / Maaş değişikliği", "salary"));
            RecordTypes.Add(new SelectionOption<string>("Diğer gelir", "income"));
        }
        else
        {
            RecordTypes.Add(new SelectionOption<string>("Kredi", "loan"));
            RecordTypes.Add(new SelectionOption<string>("Kredi kartı", "card"));
            RecordTypes.Add(new SelectionOption<string>("Geçici ödeme planı", "temporary"));
            RecordTypes.Add(new SelectionOption<string>("Taksit / finansman", "installment"));
            RecordTypes.Add(new SelectionOption<string>("Büyük planlı ödeme", "large"));
        }

        SelectedRecordType =
            RecordTypes.FirstOrDefault(x => x.Value == selected) ??
            RecordTypes.FirstOrDefault();
    }

    private void RefreshVisibleItems()
    {
        var section = IsIncomeSection
            ? ManagementSection.Income
            : ManagementSection.Payment;
        Items.Clear();
        foreach (var item in _allItems.Where(x => x.Section == section))
        {
            Items.Add(item);
        }
    }

    private void ResetForm()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Amount = string.Empty;
        Note = string.Empty;
        RemainingDebt = string.Empty;
        EarlyClosureAmount = string.Empty;
        PlanInstallments.Clear();
        CardFutureCharges.Clear();
        CardPaymentPlans.Clear();
        _cardChargeDescriptions.Clear();
        CancelEditingCard();
    }

    private string RequireName() =>
        string.IsNullOrWhiteSpace(Name)
            ? throw new InvalidOperationException("Kayıt adı gereklidir.")
            : Name.Trim();

    private static decimal RequirePositive(decimal value, string field) =>
        value > 0m
            ? value
            : throw new InvalidOperationException($"{field} sıfırdan büyük olmalıdır.");

    private static decimal? ParseOptionalMoney(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseMoney(value, "Tutar");

    private static string StrategyLabel(CreditCardPaymentStrategy strategy) =>
        strategy switch
        {
            CreditCardPaymentStrategy.AskEachStatement => "Her ekstrede sor",
            CreditCardPaymentStrategy.Minimum => "Sürekli asgari",
            CreditCardPaymentStrategy.FullStatement => "Ekstre tamamı",
            CreditCardPaymentStrategy.FixedAmount => "Sabit tutar",
            _ => "—"
        };

    private static string FallbackLabel(ProjectionFallbackStrategy strategy) =>
        strategy switch
        {
            ProjectionFallbackStrategy.None => "Belirsiz",
            ProjectionFallbackStrategy.Minimum => "Asgari varsayılıyor",
            ProjectionFallbackStrategy.FullStatement => "Tam ödeme varsayılıyor",
            ProjectionFallbackStrategy.FixedAmount => "Sabit tutar varsayılıyor",
            _ => "—"
        };
}
