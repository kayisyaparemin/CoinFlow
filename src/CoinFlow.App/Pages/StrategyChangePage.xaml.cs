using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class StrategyChangePage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public StrategyChangePage(
        SettingsViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
    }

    private async void OnApplyStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Düzen değişikliğini planla",
            "Önizlemedeki düzen seçilen maaş tarihinde başlayacak. Geçmiş kayıtlar değiştirilmeyecek.",
            "Planla",
            "Vazgeç");
        if (confirmed && await _viewModel.ApplyStrategyAsync())
        {
            await Navigation.PopModalAsync();
        }
    }

    private async void OnCancelClicked(
        object? sender,
        EventArgs eventArgs) =>
        await Navigation.PopModalAsync();
}
