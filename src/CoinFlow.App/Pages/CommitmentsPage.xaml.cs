using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CommitmentsPage : ContentPage, IQueryAttributable
{
    private readonly CommitmentsViewModel _viewModel;
    private bool _isShowingInitialStrategySetup;
    private string? _requestedSection;
    private Guid? _requestedCardId;

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
        if (string.Equals(
                _requestedSection,
                "payment",
                StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.SelectPaymentSection();
        }
        else if (string.Equals(
                     _requestedSection,
                     "income",
                     StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.SelectIncomeSection();
        }

        if (_requestedCardId is Guid cardId)
        {
            await _viewModel.EditCardAsync(cardId);
            await PageScroll.ScrollToAsync(0, 0, false);
        }

        _requestedSection = null;
        _requestedCardId = null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _requestedSection = query.TryGetValue("section", out var section)
            ? section?.ToString()
            : null;
        _requestedCardId = query.TryGetValue("cardId", out var cardId) &&
                           Guid.TryParse(cardId?.ToString(), out var parsed)
            ? parsed
            : null;
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
