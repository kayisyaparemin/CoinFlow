using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
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
        var confirmed = await DisplayAlert(
            "Verileri Sil",
            "Tüm finans verileri silinecek. Devam etmek istiyor musun?",
            "Verileri Sil",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        if (await _viewModel.ClearDevelopmentDataAsync())
        {
            await DisplayAlert(
                "Tamamlandı",
                "Tüm veriler silindi.",
                "Tamam");
        }
    }

    private async void OnLoadCanonicalSeedClicked(
        object? sender,
        EventArgs eventArgs)
    {
        if (await _viewModel.LoadCanonicalSeedAsync())
        {
            await DisplayAlert(
                "Tamamlandı",
                "Test verisi yüklendi.",
                "Tamam");
        }
    }

    private async void OnChangeStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        _viewModel.PrepareStrategyEditor();
        await Navigation.PushModalAsync(
            new StrategyChangePage(_viewModel));
    }

    private async void OnDeletePendingStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
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
