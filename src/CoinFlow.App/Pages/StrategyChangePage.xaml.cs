using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class StrategyChangePage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public StrategyChangePage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private async void OnApplyStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
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
