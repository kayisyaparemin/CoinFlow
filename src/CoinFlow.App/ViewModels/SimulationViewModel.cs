using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.ViewModels;

public partial class SimulationViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<SelectionOption<PurchaseFundingMethod>> FundingMethods { get; } =
    [
        new("Kredi kartı ile alma", PurchaseFundingMethod.CreditCard),
        new("Nakit borç ile alma", PurchaseFundingMethod.CashDebt),
        new("Krediyle alma", PurchaseFundingMethod.BankLoan),
        new("Nakit ile alma", PurchaseFundingMethod.Cash)
    ];

    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];

    [ObservableProperty] private string name = "Beyaz eşya";
    [ObservableProperty] private string totalAmount = "120000";
    [ObservableProperty] private SelectionOption<PurchaseFundingMethod>? selectedFundingMethod;
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private DateTime purchaseDate = DateTime.Today;
    [ObservableProperty] private string installmentCount = "9";
    [ObservableProperty] private DateTime firstPaymentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private string totalRepaymentAmount = string.Empty;
    [ObservableProperty] private bool isCreditCard = true;
    [ObservableProperty] private bool isFinanced = true;
    [ObservableProperty] private bool isDebtOrLoan;
    [ObservableProperty] private string methodDescription = string.Empty;
    [ObservableProperty] private string summaryText = string.Empty;
    [ObservableProperty] private string explanation = string.Empty;
    [ObservableProperty] private bool hasResults;

    public ObservableCollection<SimulationLine> Results { get; } = [];

    public async Task LoadAsync()
    {
        SelectedFundingMethod ??= FundingMethods[0];
        var selectedCardId = SelectedCreditCard?.Value;
        var data = await service.GetFinanceDataAsync();
        CreditCards.Clear();
        foreach (var card in data.CreditCards)
        {
            var available = Math.Max(0m, card.Limit - card.CurrentTotalDebt);
            CreditCards.Add(new SelectionOption<Guid>(
                $"{card.Bank} {card.Name} • Borç {Money(card.CurrentTotalDebt)} • Kullanılabilir {Money(available)}".Trim(),
                card.Id));
        }

        SelectedCreditCard = CreditCards.FirstOrDefault(x => x.Value == selectedCardId)
            ?? CreditCards.FirstOrDefault();
    }

    partial void OnSelectedFundingMethodChanged(SelectionOption<PurchaseFundingMethod>? value)
    {
        var method = value?.Value ?? PurchaseFundingMethod.CreditCard;
        IsCreditCard = method == PurchaseFundingMethod.CreditCard;
        IsFinanced = method != PurchaseFundingMethod.Cash;
        IsDebtOrLoan = method is PurchaseFundingMethod.CashDebt or PurchaseFundingMethod.BankLoan;
        MethodDescription = method switch
        {
            PurchaseFundingMethod.CreditCard => "Seçilen kartın güncel borcu, gelecek taksitleri ve asgari/manüel ödeme düzeni birlikte hesaplanır.",
            PurchaseFundingMethod.CashDebt => "Alışveriş anında nakit azalmaz; borç geri ödemeleri mevcut zorunlu ödemelere eklenir.",
            PurchaseFundingMethod.BankLoan => "Faiz ve masraflar dahil toplam geri ödeme, seçilen vadeye bölünerek mevcut kredilere eklenir.",
            PurchaseFundingMethod.Cash => "Tutar, alışveriş tarihini içeren maaş döneminin serbest parasından tek seferde düşülür.",
            _ => string.Empty
        };
        HasResults = false;
        Results.Clear();
    }

    [RelayCommand]
    private async Task CalculateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var method = SelectedFundingMethod?.Value ?? PurchaseFundingMethod.CreditCard;
            var count = 1;
            if (method != PurchaseFundingMethod.Cash && !int.TryParse(InstallmentCount, out count))
            {
                throw new InvalidOperationException("Taksit sayısı geçerli bir tam sayı olmalıdır.");
            }

            decimal? repaymentTotal = null;
            if (IsDebtOrLoan && !string.IsNullOrWhiteSpace(TotalRepaymentAmount))
            {
                repaymentTotal = ParseMoney(TotalRepaymentAmount, "Toplam geri ödeme");
            }

            var result = await service.SimulatePurchaseAsync(new PurchaseSimulationRequest(
                Name.Trim(),
                ParseMoney(TotalAmount, "Toplam tutar"),
                method,
                DateOnly.FromDateTime(PurchaseDate),
                count,
                method == PurchaseFundingMethod.Cash
                    ? DateOnly.FromDateTime(PurchaseDate)
                    : DateOnly.FromDateTime(FirstPaymentDate),
                IsCreditCard ? SelectedCreditCard?.Value : null,
                repaymentTotal));

            Results.Clear();
            foreach (var row in result.Rows)
            {
                Results.Add(new SimulationLine(
                    row.Month.ToString("MMMM yyyy", TurkishCulture),
                    Money(row.BaselineObligations),
                    Money(row.BaselineSpendable),
                    Money(row.NewPayment, 2),
                    Money(row.ResultingObligations, 2),
                    Money(row.ResultingSpendable, 2),
                    Money(row.RemainingNewDebt, 2)));
            }

            SummaryText = $"Mevcut 12 aylık zorunlu ödeme: {Money(result.ExistingObligationsInHorizon)} • " +
                          $"Bu seçimden doğan 12 aylık ek ödeme: {Money(result.NewPaymentsInHorizon, 2)} • " +
                          $"12. dönem sonunda kalan yeni borç: {Money(result.RemainingNewDebtAfterHorizon, 2)}";
            Explanation = result.Explanation;
            HasResults = true;
        }
        catch (Exception exception)
        {
            HasResults = false;
            SetStatus(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
