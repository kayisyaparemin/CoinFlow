using System.Collections.ObjectModel;
using System.Globalization;
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

    public ObservableCollection<SummaryLine> Items { get; } = [];

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

    [ObservableProperty] private string installmentsText = string.Empty;

    [ObservableProperty] private string cardLimit = string.Empty;
    [ObservableProperty] private string statementRemaining = string.Empty;
    [ObservableProperty] private string cycleSpending = string.Empty;
    [ObservableProperty] private string closingDay = "25";
    [ObservableProperty] private string dueDay = "5";
    [ObservableProperty] private string minimumRate = "40";
    [ObservableProperty] private bool useManualPayment;
    [ObservableProperty] private string manualPayment = string.Empty;
    [ObservableProperty] private string futureCardInstallments = string.Empty;

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

        Items.Clear();
        foreach (var salary in data.Salaries.OrderByDescending(x => x.EffectiveFrom))
        {
            Items.Add(new SummaryLine("Maaş", $"{salary.EffectiveFrom:dd.MM.yyyy} itibarıyla", Money(salary.NetAmount), salary.Note));
        }
        foreach (var loan in data.Loans)
        {
            Items.Add(new SummaryLine($"{loan.Bank} {loan.Name}".Trim(), $"Her ayın {loan.PaymentDay}. günü", Money(loan.MonthlyInstallment), $"{loan.InstallmentCount ?? 0} taksit kaldı"));
        }
        foreach (var plan in data.PaymentPlans)
        {
            Items.Add(new SummaryLine(plan.Name, $"{plan.Installments.Count} ödeme", Money(plan.Installments.Sum(x => x.Amount)), plan.Kind == PaymentPlanKind.Temporary ? "Geçici" : "Planlı"));
        }
        foreach (var card in data.CreditCards)
        {
            Items.Add(new SummaryLine($"{card.Bank} {card.Name}".Trim(), $"Son ödeme: ayın {card.PaymentDueDay}. günü", Money(card.CurrentTotalDebt), card.PaymentMode == CreditCardPaymentMode.Minimum ? $"%{card.MinimumPaymentRate * 100:N0} asgari" : "Manuel"));
        }
    }

    partial void OnSelectedTypeChanged(SelectionOption<string>? value)
    {
        IsSalary = value?.Value == "salary";
        IsLoan = value?.Value == "loan";
        IsPlan = value?.Value == "plan";
        IsCard = value?.Value == "card";
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
        var planId = Guid.NewGuid();
        var installments = ParseDatedAmounts(InstallmentsText)
            .Select(x => new TemporaryPaymentInstallment { PlanId = planId, DueDate = x.Date, Amount = x.Amount })
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
        var cardId = Guid.NewGuid();
        var future = ParseDatedAmounts(FutureCardInstallments, allowEmpty: true)
            .Select(x => new CardInstallment { CreditCardId = cardId, Description = "Kart taksiti", DueDate = x.Date, Amount = x.Amount })
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
            LastStatementDebt = 0m,
            LastStatementRemaining = statementRemaining,
            CurrentCycleSpending = cycleSpending,
            StatementClosingDay = ParseDay(ClosingDay, "Kesim günü"),
            PaymentDueDay = ParseDay(DueDay, "Son ödeme günü"),
            MinimumPaymentRate = rate,
            PaymentMode = UseManualPayment ? CreditCardPaymentMode.Manual : CreditCardPaymentMode.Minimum,
            ManualPaymentAmount = UseManualPayment ? RequirePositive(ParseMoney(ManualPayment, "Manuel ödeme"), "Manuel ödeme") : null,
            FutureInstallments = future
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

        InstallmentsText = string.Empty;

        CardLimit = string.Empty;
        StatementRemaining = string.Empty;
        CycleSpending = string.Empty;
        ClosingDay = "25";
        DueDay = "5";
        MinimumRate = "40";
        UseManualPayment = false;
        ManualPayment = string.Empty;
        FutureCardInstallments = string.Empty;
    }

    private static IReadOnlyList<(DateOnly Date, decimal Amount)> ParseDatedAmounts(string input, bool allowEmpty = false)
    {
        var lines = input.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 && allowEmpty)
        {
            return [];
        }

        var result = new List<(DateOnly, decimal)>();
        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !DateOnly.TryParseExact(line[..separator].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new InvalidOperationException("Taksitler yyyy-MM-dd:tutar biçiminde, satır satır girilmelidir.");
            }

            var amount = ParseMoney(line[(separator + 1)..].Trim(), "Taksit tutarı");
            result.Add((date, RequirePositive(amount, "Taksit tutarı")));
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("En az bir taksit girilmelidir.");
        }
        return result;
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
}
