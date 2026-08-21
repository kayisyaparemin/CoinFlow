using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class PeriodReviewPage : ContentPage
{
    private readonly PeriodReviewWizardViewModel _viewModel;

    public PeriodReviewPage(PeriodReviewWizardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = TryCloseAsync();
        return true;
    }

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await TryCloseAsync();

    private async Task TryCloseAsync()
    {
        if (!_viewModel.IsSuccess)
        {
            var close = await DisplayAlert(
                "Güncellemeden çıkılsın mı?",
                "Girdiğin bilgiler henüz kaydedilmedi. Çıkmak istiyor musun?",
                "Çık",
                "Devam et");
            if (!close)
            {
                return;
            }
        }

        await Navigation.PopModalAsync();
    }

    private async void OnViewPlanClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
        await Shell.Current.GoToAsync("//projection/future-months-content");
    }

    private async void OnReturnHomeClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
        await Shell.Current.GoToAsync("//dashboard/dashboard-content");
    }
}
