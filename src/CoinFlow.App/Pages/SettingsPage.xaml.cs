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

    private async void OnResetDevelopmentDataClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
            "Development verisini yeniden yükle",
            "Development veritabanındaki tüm kayıtlar silinecek ve canonical örnek veri yeniden oluşturulacak.",
            "Sıfırla ve yükle",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        if (await _viewModel.ResetDevelopmentDataAsync())
        {
            await DisplayAlert(
                "Tamamlandı",
                "Canonical development verisi yeniden yüklendi.",
                "Tamam");
        }
    }
}
