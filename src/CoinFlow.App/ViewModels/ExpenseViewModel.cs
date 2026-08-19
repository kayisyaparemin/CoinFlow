using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoinFlow.App.Models;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.ViewModels;

public partial class ExpenseViewModel(CoinFlowService service) : ViewModelBase
{
    public ObservableCollection<SelectionOption<ExpenseCategory>> Categories { get; } =
    [
        new("Yemek", ExpenseCategory.Food), new("Yakıt", ExpenseCategory.Fuel),
        new("Market", ExpenseCategory.Grocery), new("Araba", ExpenseCategory.Car),
        new("Eğlence", ExpenseCategory.Entertainment), new("Ev", ExpenseCategory.Home),
        new("Hediye", ExpenseCategory.Gift), new("Fatura", ExpenseCategory.Bill),
        new("Diğer", ExpenseCategory.Other)
    ];

    public ObservableCollection<SelectionOption<ExpensePaymentType>> PaymentTypes { get; } =
    [
        new("Nakit", ExpensePaymentType.Cash), new("Kredi kartı", ExpensePaymentType.CreditCard),
        new("Yeni taksit", ExpensePaymentType.NewInstallment), new("Başka", ExpensePaymentType.Other)
    ];

    public ObservableCollection<SelectionOption<Guid>> CreditCards { get; } = [];

    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private DateTime expenseDate = DateTime.Today;
    [ObservableProperty] private SelectionOption<ExpenseCategory>? selectedCategory;
    [ObservableProperty] private SelectionOption<ExpensePaymentType>? selectedPaymentType;
    [ObservableProperty] private SelectionOption<Guid>? selectedCreditCard;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string installmentCount = "3";
    [ObservableProperty] private DateTime firstInstallmentDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private bool isCreditCard;
    [ObservableProperty] private bool isInstallment;

    public async Task LoadAsync()
    {
        SelectedCategory ??= Categories[0];
        SelectedPaymentType ??= PaymentTypes[0];
        var data = await service.GetFinanceDataAsync();
        CreditCards.Clear();
        foreach (var card in data.CreditCards)
        {
            CreditCards.Add(new SelectionOption<Guid>($"{card.Bank} {card.Name}".Trim(), card.Id));
        }
        SelectedCreditCard ??= CreditCards.FirstOrDefault();
    }

    partial void OnSelectedPaymentTypeChanged(SelectionOption<ExpensePaymentType>? value)
    {
        IsCreditCard = value?.Value == ExpensePaymentType.CreditCard;
        IsInstallment = value?.Value == ExpensePaymentType.NewInstallment;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            SetStatus(string.Empty);
            var paymentType = SelectedPaymentType?.Value ?? ExpensePaymentType.Cash;
            var count = IsInstallment && int.TryParse(InstallmentCount, out var parsedCount) ? parsedCount : (int?)null;
            await service.AddExpenseAsync(new ExpenseDraft(
                ParseMoney(Amount, "Tutar"),
                DateOnly.FromDateTime(ExpenseDate),
                SelectedCategory?.Value ?? ExpenseCategory.Other,
                paymentType,
                Note,
                IsCreditCard ? SelectedCreditCard?.Value : null,
                count,
                IsInstallment ? DateOnly.FromDateTime(FirstInstallmentDate) : null));

            Amount = string.Empty;
            Note = string.Empty;
            SetStatus("Harcama kaydedildi. Bütçe görünümü güncellendi.");
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
}
