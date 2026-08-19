using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class CommitmentsViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<SelectionOption<string>> Types { get; } =
    [
        new("Maaş ekle / değiştir", "salary"), new("Kredi", "loan"),
        new("Kredi kartı", "card"), new("Geçici ödeme planı", "plan")
    ];

    public ObservableCollection<SelectionOption<CreditCardPaymentStrategy>> PaymentStrategies { get; } =
    [
        new("Her ekstrede bana sor", CreditCardPaymentStrategy.AskEachStatement),
        new("Her zaman asgari ödeme", CreditCardPaymentStrategy.Minimum),
        new("Her zaman ekstre tamamı", CreditCardPaymentStrategy.FullStatement),
        new("Sabit tutar", CreditCardPaymentStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<ProjectionFallbackStrategy>> ProjectionFallbackStrategies { get; } =
    [
        new("Tahmin yapma", ProjectionFallbackStrategy.None),
        new("Asgari ödeme varsay", ProjectionFallbackStrategy.Minimum),
        new("Tam ödeme varsay", ProjectionFallbackStrategy.FullStatement),
        new("Sabit tutar varsay", ProjectionFallbackStrategy.FixedAmount)
    ];

    public ObservableCollection<SelectionOption<CreditCardPaymentType>> PaymentPlanTypes { get; } =
    [
        new("Asgari ödeme", CreditCardPaymentType.Minimum),
        new("Ekstrenin tamamı", CreditCardPaymentType.FullStatement),
        new("Özel tutar", CreditCardPaymentType.FixedAmount)
    ];

    public ObservableCollection<CommitmentSummaryLine> Items { get; } = [];
    public ObservableCollection<DatedAmountLine> PlanInstallments { get; } = [];
    public ObservableCollection<DatedAmountLine> CardFuturePayments { get; } = [];
    public ObservableCollection<CardPaymentPlanLine> CardPaymentPlans { get; } = [];

    private Guid? _editingCardId;
    private DateOnly? _editingCardBalanceAsOfDate;
    private readonly Dictionary<Guid, string> _editingCardChargeDescriptions = [];

    [ObservableProperty] private SelectionOption<string>? selectedType;
    [ObservableProperty] private bool isSalary;
    [ObservableProperty] private bool isLoan;
    [ObservableProperty] private bool isPlan;
    [ObservableProperty] private bool isCard;
    [ObservableProperty] private bool hasNoSalary;

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string bank = string.Empty;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime effectiveDate = DateTime.Today;

    [ObservableProperty] private string paymentDay = "10";
    [ObservableProperty] private DateTime loanStartDate = DateTime.Today;
    [ObservableProperty] private string installmentCount = "12";
    [ObservableProperty] private string remainingDebt = string.Empty;
    [ObservableProperty] private string earlyClosureAmount = string.Empty;

    [ObservableProperty] private DateTime planInstallmentDate = DateTime.Today;
    [ObservableProperty] private string planInstallmentAmount = string.Empty;

    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private string statementRemaining = string.Empty;
    [ObservableProperty] private string cycleSpending = string.Empty;
    [ObservableProperty] private string closingDay = "25";
    [ObservableProperty] private string dueDay = "5";
    [ObservableProperty] private string minimumRate = "40";
    [ObservableProperty] private DateTime cardFuturePaymentDate = DateTime.Today;
    [ObservableProperty] private string cardFuturePaymentAmount = string.Empty;
    [ObservableProperty] private SelectionOption<CreditCardPaymentStrategy>? selectedPaymentStrategy;
    [ObservableProperty] private string fixedPaymentAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentStrategy;
    [ObservableProperty] private SelectionOption<ProjectionFallbackStrategy>? selectedProjectionFallbackStrategy;
    [ObservableProperty] private string projectionFallbackFixedAmount = string.Empty;
    [ObservableProperty] private bool isFixedProjectionFallback;
    [ObservableProperty] private DateTime cardPaymentPlanDate = DateTime.Today;
    [ObservableProperty] private SelectionOption<CreditCardPaymentType>? selectedPaymentPlanType;
    [ObservableProperty] private string cardPaymentPlanAmount = string.Empty;
    [ObservableProperty] private bool isFixedPaymentPlan;
    [ObservableProperty] private bool isEditingCard;
    [ObservableProperty] private string saveButtonText = "Kaydet";

    public async Task LoadAsync()
    {
        var data = await service.GetFinanceDataAsync();
        HasNoSalary = data.Salaries.Count == 0;
        if (HasNoSalary)
        {
            SelectedType = Types.First(x => x.Value == "salary");
        }
        else
        {
            SelectedType ??= Types[0];
        }
        SelectedPaymentStrategy ??= PaymentStrategies[0];
        SelectedProjectionFallbackStrategy ??= ProjectionFallbackStrategies[0];
        SelectedPaymentPlanType ??= PaymentPlanTypes[0];

        Items.Clear();
        foreach (var salary in data.Salaries.OrderByDescending(x => x.EffectiveFrom))
        {
            Items.Add(new CommitmentSummaryLine(
                salary.Id, CommitmentKind.Salary, "Maaş", $"{salary.EffectiveFrom:dd.MM.yyyy} itibarıyla",
                Money(salary.NetAmount), salary.Note));
        }
        foreach (var loan in data.Loans)
        {
            Items.Add(new CommitmentSummaryLine(
                loan.Id, CommitmentKind.Loan, $"{loan.Bank} {loan.Name}".Trim(),
                $"Her ayın {loan.PaymentDay}. günü", Money(loan.MonthlyInstallment),
                $"{loan.InstallmentCount ?? 0} taksit kaldı"));
        }
        foreach (var plan in data.PaymentPlans)
        {
            Items.Add(new CommitmentSummaryLine(
                plan.Id, CommitmentKind.PaymentPlan, plan.Name, $"{plan.Installments.Count} ödeme",
                Money(plan.Installments.Sum(x => x.Amount)),
                plan.Kind == PaymentPlanKind.Temporary ? "Geçici" : "Planlı"));
        }
        foreach (var card in data.CreditCards)
        {
            Items.Add(new CommitmentSummaryLine(
                card.Id,
                CommitmentKind.CreditCard,
                $"{card.Bank} {card.Name}".Trim(),
                $"Kesim {card.StatementClosingDay} • Son ödeme {card.PaymentDueDay}",
                Money(card.CurrentTotalDebt),
                StrategyLabel(card)));
        }
    }

    partial void OnSelectedTypeChanged(SelectionOption<string>? value)
    {
        IsSalary = value?.Value == "salary";
        IsLoan = value?.Value == "loan";
        IsPlan = value?.Value == "plan";
        IsCard = value?.Value == "card";
    }

    partial void OnSelectedPaymentStrategyChanged(SelectionOption<CreditCardPaymentStrategy>? value) =>
        IsFixedPaymentStrategy = value?.Value == CreditCardPaymentStrategy.FixedAmount;

    partial void OnSelectedProjectionFallbackStrategyChanged(SelectionOption<ProjectionFallbackStrategy>? value) =>
        IsFixedProjectionFallback = value?.Value == ProjectionFallbackStrategy.FixedAmount;

    partial void OnSelectedPaymentPlanTypeChanged(SelectionOption<CreditCardPaymentType>? value) =>
        IsFixedPaymentPlan = value?.Value == CreditCardPaymentType.FixedAmount;

    [RelayCommand]
    private void AddPlanInstallment()
    {
        try
        {
            var amount = RequirePositive(ParseMoney(PlanInstallmentAmount, "Taksit tutarı"), "Taksit tutarı");
            PlanInstallments.Add(new DatedAmountLine(
                Guid.NewGuid(), DateOnly.FromDateTime(PlanInstallmentDate), amount));
            PlanInstallmentAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    [RelayCommand]
    private void AddCardFuturePayment()
    {
        try
        {
            var amount = RequirePositive(
                ParseMoney(CardFuturePaymentAmount, "Gelecek dönem tutarı"),
                "Gelecek dönem tutarı");
            CardFuturePayments.Add(new DatedAmountLine(
                Guid.NewGuid(), DateOnly.FromDateTime(CardFuturePaymentDate), amount));
            CardFuturePaymentAmount = string.Empty;
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
            var paymentType = SelectedPaymentPlanType?.Value
                ?? throw new InvalidOperationException("Özel ödeme şekli seçilmelidir.");
            var amount = paymentType == CreditCardPaymentType.FixedAmount
                ? RequirePositive(ParseMoney(CardPaymentPlanAmount, "Özel ödeme tutarı"), "Özel ödeme tutarı")
                : (decimal?)null;
            var dueDate = DateOnly.FromDateTime(CardPaymentPlanDate);
            var existing = CardPaymentPlans.FirstOrDefault(x => x.DueDate == dueDate);
            if (existing is not null)
            {
                CardPaymentPlans.Remove(existing);
            }

            CardPaymentPlans.Add(new CardPaymentPlanLine(
                existing?.Id ?? Guid.NewGuid(), dueDate, paymentType, amount));
            CardPaymentPlanAmount = string.Empty;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    public void RemovePlanInstallment(DatedAmountLine line) => PlanInstallments.Remove(line);

    public void RemoveCardFuturePayment(DatedAmountLine line) => CardFuturePayments.Remove(line);

    public void RemoveCardPaymentPlan(CardPaymentPlanLine line) => CardPaymentPlans.Remove(line);

    public async Task EditCardAsync(Guid cardId)
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var card = (await service.GetFinanceDataAsync()).CreditCards.Single(x => x.Id == cardId);
            _editingCardId = card.Id;
            _editingCardBalanceAsOfDate = card.BalanceAsOfDate;
            IsEditingCard = true;
            SaveButtonText = "Kartı güncelle";
            SelectedType = Types.First(x => x.Value == "card");
            Name = card.Name;
            Bank = card.Bank;
            CardLimit = InputMoney(card.Limit);
            StatementRemaining = InputMoney(card.CarriedBalance);
            CycleSpending = InputMoney(card.UnbilledSpending);
            ClosingDay = card.StatementClosingDay.ToString(TurkishCulture);
            DueDay = card.PaymentDueDay.ToString(TurkishCulture);
            MinimumRate = InputMoney(card.MinimumPaymentRate * 100m);
            SelectedPaymentStrategy = PaymentStrategies.First(x => x.Value == card.PaymentStrategy);
            FixedPaymentAmount = card.FixedPaymentAmount is null ? string.Empty : InputMoney(card.FixedPaymentAmount.Value);
            SelectedProjectionFallbackStrategy = ProjectionFallbackStrategies
                .First(x => x.Value == card.ProjectionFallbackStrategy);
            ProjectionFallbackFixedAmount = card.ProjectionFallbackFixedAmount is null
                ? string.Empty
                : InputMoney(card.ProjectionFallbackFixedAmount.Value);

            CardFuturePayments.Clear();
            _editingCardChargeDescriptions.Clear();
            foreach (var charge in card.Charges.OrderBy(x => x.PostingDate))
            {
                _editingCardChargeDescriptions[charge.Id] = charge.Description;
                CardFuturePayments.Add(new DatedAmountLine(charge.Id, charge.PostingDate, charge.Amount));
            }

            CardPaymentPlans.Clear();
            foreach (var plan in card.PaymentPlans.OrderBy(x => x.DueDate))
            {
                CardPaymentPlans.Add(new CardPaymentPlanLine(
                    plan.Id, plan.DueDate, plan.PaymentType, plan.Amount));
            }

            SetStatus("Kart ayarları forma yüklendi. Değişiklikleri yaptıktan sonra güncelle.");
        }
        catch (Exception exception)
        {
            SetStatus($"Kart düzenlemeye açılamadı: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteAsync(CommitmentSummaryLine item)
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            switch (item.Kind)
            {
                case CommitmentKind.Salary:
                    await service.DeleteSalaryAsync(item.Id);
                    break;
                case CommitmentKind.Loan:
                    await service.DeleteLoanAsync(item.Id);
                    break;
                case CommitmentKind.PaymentPlan:
                    await service.DeletePaymentPlanAsync(item.Id);
                    break;
                case CommitmentKind.CreditCard:
                    await service.DeleteCreditCardAsync(item.Id);
                    break;
                default:
                    throw new InvalidOperationException("Silinecek kayıt türü tanınmıyor.");
            }

            await LoadAsync();
            SetStatus($"{item.Title} silindi.");
        }
        catch (Exception exception)
        {
            SetStatus($"Kayıt silinemedi: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            switch (SelectedType?.Value)
            {
                case "salary":
                    await SaveSalaryAsync();
                    break;
                case "loan":
                    await SaveLoanAsync();
                    break;
                case "plan":
                    await SavePlanAsync();
                    break;
                case "card":
                    await SaveCardAsync();
                    break;
                default:
                    throw new InvalidOperationException("Kayıt türü seçilmelidir.");
            }

            var savedType = SelectedType?.Label ?? "Kayıt";
            ClearForm();
            SetStatus($"{savedType} kaydedildi ve bütçe projeksiyonuna eklendi.");
            await LoadAsync();
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

    private Task SaveSalaryAsync() => service.SaveSalaryAsync(new SalaryScheduleEntry
    {
        NetAmount = RequirePositive(ParseMoney(Amount, "Net maaş"), "Net maaş"),
        EffectiveFrom = DateOnly.FromDateTime(EffectiveDate),
        Note = Name.Trim()
    });

    private Task SaveLoanAsync()
    {
        var day = ParseDay(PaymentDay, "Ödeme günü");
        if (!int.TryParse(InstallmentCount, out var count) || count < 1)
        {
            throw new InvalidOperationException("Taksit sayısı en az 1 olmalıdır.");
        }

        return service.SaveLoanAsync(new Loan
        {
            Name = RequireText(Name, "Kredi adı"),
            Bank = Bank.Trim(),
            MonthlyInstallment = RequirePositive(ParseMoney(Amount, "Aylık taksit"), "Aylık taksit"),
            PaymentDay = day,
            StartDate = DateOnly.FromDateTime(LoanStartDate),
            InstallmentCount = count,
            RemainingDebt = OptionalMoney(RemainingDebt),
            EarlyClosureAmount = OptionalMoney(EarlyClosureAmount)
        });
    }

    private Task SavePlanAsync()
    {
        if (PlanInstallments.Count == 0)
        {
            throw new InvalidOperationException("En az bir taksit eklenmelidir.");
        }

        var planId = Guid.NewGuid();
        var installments = PlanInstallments
            .OrderBy(x => x.Date)
            .Select(x => new TemporaryPaymentInstallment
            {
                PlanId = planId,
                DueDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        return service.SavePaymentPlanAsync(new TemporaryPaymentPlan
        {
            Id = planId,
            Name = RequireText(Name, "Plan adı"),
            Kind = PaymentPlanKind.Temporary,
            Installments = installments
        });
    }

    private Task SaveCardAsync()
    {
        var cardId = _editingCardId ?? Guid.NewGuid();
        var future = CardFuturePayments
            .OrderBy(x => x.Date)
            .Select(x => new CardCharge
            {
                Id = x.Id,
                CreditCardId = cardId,
                Description = _editingCardChargeDescriptions.GetValueOrDefault(x.Id, "Gelecek dönem ödemesi"),
                PostingDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        var paymentStrategy = SelectedPaymentStrategy?.Value
            ?? CreditCardPaymentStrategy.AskEachStatement;
        var fixedAmount = paymentStrategy == CreditCardPaymentStrategy.FixedAmount
            ? RequirePositive(ParseMoney(FixedPaymentAmount, "Sabit ödeme tutarı"), "Sabit ödeme tutarı")
            : (decimal?)null;
        var fallbackStrategy = SelectedProjectionFallbackStrategy?.Value
            ?? ProjectionFallbackStrategy.None;
        var fallbackFixedAmount = fallbackStrategy == ProjectionFallbackStrategy.FixedAmount
            ? RequirePositive(
                ParseMoney(ProjectionFallbackFixedAmount, "Sabit tahmin tutarı"),
                "Sabit tahmin tutarı")
            : (decimal?)null;
        var paymentPlans = CardPaymentPlans
            .OrderBy(x => x.DueDate)
            .Select(x => new CreditCardPaymentPlan
            {
                Id = x.Id,
                CreditCardId = cardId,
                DueDate = x.DueDate,
                PaymentType = x.PaymentType,
                Amount = x.Amount
            })
            .ToArray();
        var rate = ParseMoney(MinimumRate, "Asgari oran") / 100m;
        if (rate is < 0m or > 1m)
        {
            throw new InvalidOperationException("Asgari oran 0 ile 100 arasında olmalıdır.");
        }

        var statementRemaining = RequireNonNegative(
            ParseMoney(StatementRemaining, "Son ekstreden kalan"),
            "Son ekstreden kalan");
        var cycleSpending = RequireNonNegative(
            ParseMoney(CycleSpending, "Dönem içi harcama"),
            "Dönem içi harcama");

        return service.SaveCreditCardAsync(new CreditCard
        {
            Id = cardId,
            Name = RequireText(Name, "Kart adı"),
            Bank = Bank.Trim(),
            Limit = RequirePositive(ParseMoney(CardLimit, "Kart limiti"), "Kart limiti"),
            CurrentTotalDebt = 0m,
            CarriedBalance = statementRemaining,
            UnbilledSpending = cycleSpending,
            BalanceAsOfDate = _editingCardBalanceAsOfDate ?? default,
            StatementClosingDay = ParseDay(ClosingDay, "Kesim günü"),
            PaymentDueDay = ParseDay(DueDay, "Son ödeme günü"),
            MinimumPaymentRate = rate,
            PaymentStrategy = paymentStrategy,
            FixedPaymentAmount = fixedAmount,
            ProjectionFallbackStrategy = fallbackStrategy,
            ProjectionFallbackFixedAmount = fallbackFixedAmount,
            Charges = future,
            PaymentPlans = paymentPlans
        });
    }

    private void ClearForm()
    {
        Name = string.Empty;
        Bank = string.Empty;
        Amount = string.Empty;
        EffectiveDate = DateTime.Today;

        PaymentDay = "10";
        LoanStartDate = DateTime.Today;
        InstallmentCount = "12";
        RemainingDebt = string.Empty;
        EarlyClosureAmount = string.Empty;

        PlanInstallmentDate = DateTime.Today;
        PlanInstallmentAmount = string.Empty;
        PlanInstallments.Clear();

        CardLimit = string.Empty;
        StatementRemaining = string.Empty;
        CycleSpending = string.Empty;
        ClosingDay = "25";
        DueDay = "5";
        MinimumRate = "40";
        CardFuturePaymentDate = DateTime.Today;
        CardFuturePaymentAmount = string.Empty;
        CardFuturePayments.Clear();
        SelectedPaymentStrategy = PaymentStrategies[0];
        FixedPaymentAmount = string.Empty;
        SelectedProjectionFallbackStrategy = ProjectionFallbackStrategies[0];
        ProjectionFallbackFixedAmount = string.Empty;
        CardPaymentPlanDate = DateTime.Today;
        SelectedPaymentPlanType = PaymentPlanTypes[0];
        CardPaymentPlanAmount = string.Empty;
        CardPaymentPlans.Clear();
        _editingCardId = null;
        _editingCardBalanceAsOfDate = null;
        _editingCardChargeDescriptions.Clear();
        IsEditingCard = false;
        SaveButtonText = "Kaydet";
    }

    private static int ParseDay(string value, string field)
    {
        if (!int.TryParse(value, out var day) || day is < 1 or > 31)
        {
            throw new InvalidOperationException($"{field} 1 ile 31 arasında olmalıdır.");
        }
        return day;
    }

    private static string RequireText(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{field} gereklidir.") : value.Trim();

    private static decimal RequirePositive(decimal value, string field) =>
        value <= 0m ? throw new InvalidOperationException($"{field} sıfırdan büyük olmalıdır.") : value;

    private static decimal RequireNonNegative(decimal value, string field) =>
        value < 0m ? throw new InvalidOperationException($"{field} negatif olamaz.") : value;

    private static decimal? OptionalMoney(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseMoney(value, "Opsiyonel tutar");

    private static string InputMoney(decimal value) =>
        value.ToString("0.##", TurkishCulture);

    private static string StrategyLabel(CreditCard card) => card.PaymentStrategy switch
    {
        CreditCardPaymentStrategy.AskEachStatement => "Her ekstrede sor",
        CreditCardPaymentStrategy.Minimum => "Sürekli asgari",
        CreditCardPaymentStrategy.FullStatement => "Ekstre tamamı",
        CreditCardPaymentStrategy.FixedAmount => $"Sabit {Money(card.FixedPaymentAmount.GetValueOrDefault())}",
        _ => "Ödeme stratejisi"
    };
}
