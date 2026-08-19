using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CommitmentsPage : ContentPage
{
    private readonly CommitmentsViewModel _viewModel;

    public CommitmentsPage(CommitmentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void OnRemovePlanInstallmentClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemovePlanInstallment(line);
        }
    }

    private void OnRemoveCardFuturePaymentClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemoveCardFuturePayment(line);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: CommitmentSummaryLine item })
        {
            return;
        }

        var confirmed = await DisplayAlert(
            "Kaydı sil",
            $"{item.Title} kalıcı olarak silinsin mi?",
            "Sil",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(item);
        }
    }
}
