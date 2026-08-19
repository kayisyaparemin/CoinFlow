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

    private async void OnResetAllDataClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Tüm veriler silinsin mi?",
            "Bu işlem geri alınamaz. Maaşlar, borçlar, kartlar, planlar, harcamalar, tampon ve ayarlar tamamen silinecek.",
            "Tümünü sil",
            "Vazgeç");

        if (!confirmed)
        {
            return;
        }

        if (await _viewModel.ResetAllDataAsync())
        {
            await DisplayAlert("Sıfırlama tamamlandı", "Uygulamadaki tüm veriler silindi.", "Tamam");
        }
    }
}
