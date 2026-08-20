using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class SimulationPage : ContentPage
{
    private readonly SimulationViewModel _viewModel;

    public SimulationPage(SimulationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnApplyPlanClicked(object? sender, EventArgs eventArgs)
    {
        var confirmed = await DisplayAlert(
            "Planı uygula",
            "Bu senaryo gerçek finansal kayıtlarına eklenecek. Devam edilsin mi?",
            "Uygula",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        if (await _viewModel.ApplyLastPlanAsync())
        {
            await DisplayAlert(
                "Plan uygulandı",
                "Yeni plan Gelir & Ödemeler kayıtlarına eklendi.",
                "Tamam");
        }
    }
}
