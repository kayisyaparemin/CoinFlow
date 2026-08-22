using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public SettingsPage(
        SettingsViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnClearDevelopmentDataClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Verileri Sil",
            "Tüm finans verileri silinecek. Devam etmek istiyor musun?",
            "Verileri Sil",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        await _viewModel.ClearDevelopmentDataAsync();
    }

    private async void OnLoadCanonicalSeedClicked(
        object? sender,
        EventArgs eventArgs)
    {
        await _viewModel.LoadCanonicalSeedAsync();
    }

    private async void OnChangeStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        _viewModel.PrepareStrategyEditor();
        await Navigation.PushModalAsync(
            new StrategyChangePage(_viewModel, _feedback));
    }

    private async void OnDeletePendingStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Planlanan değişikliği sil",
            "Henüz başlamamış düzen değişikliği silinsin mi?",
            "Sil",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.DeletePendingStrategyAsync();
        }
    }

}
