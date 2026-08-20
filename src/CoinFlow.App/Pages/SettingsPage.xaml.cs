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

    private async void OnApplyStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
            "Düzen değişikliğini planla",
            "Önizlemedeki düzen seçilen maaş tarihinde başlayacak. Geçmiş kayıtlar değiştirilmeyecek.",
            "Planla",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.ApplyStrategyAsync();
        }
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

    private async void OnCorrectHistoricalStrategyClicked(
        object? sender,
        EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
            "Geçmiş kaydı düzelt",
            "Bu işlem geçmiş projection sonuçlarını değiştirebilir. Seçilen geçmiş düzen kaydı düzeltilecek. Devam edilsin mi?",
            "Geçmişi düzelt",
            "Vazgeç");
        if (confirmed)
        {
            await _viewModel.CorrectHistoricalStrategyAsync();
        }
    }
}
