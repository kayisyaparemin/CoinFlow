using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CommitmentsPage : ContentPage
{
    private readonly CommitmentsViewModel _viewModel;
    private bool _isShowingInitialStrategySetup;

    public CommitmentsPage(CommitmentsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.InitialStrategySetupRequested +=
            OnInitialStrategySetupRequested;
    }

    private async void OnInitialStrategySetupRequested(
        CoinFlow.Application.Models.InitialPaymentStrategySetup setup)
    {
        if (_isShowingInitialStrategySetup)
        {
            return;
        }

        _isShowingInitialStrategySetup = true;
        try
        {
            var page = new InitialStrategyPage(setup, _viewModel);
            await Navigation.PushModalAsync(page);
            await page.Completion;
        }
        finally
        {
            _isShowingInitialStrategySetup = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void OnRemovePlanPaymentClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemovePlanPayment(line);
        }
    }

    private void OnRemoveCardChargeClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: DatedAmountLine line })
        {
            _viewModel.RemoveCardCharge(line);
        }
    }

    private void OnRemoveCardPaymentPlanClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: CardPaymentPlanLine line })
        {
            _viewModel.RemoveCardPaymentPlan(line);
        }
    }

    private async void OnEditCardClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Button
            {
                CommandParameter: FinancialRecordLine item
            } ||
            !item.CanEditCard)
        {
            return;
        }

        await _viewModel.EditCardAsync(item.Id);
        await PageScroll.ScrollToAsync(0, 0, true);
    }

    private async void OnDeleteClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not Button
            {
                CommandParameter: FinancialRecordLine item
            })
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
