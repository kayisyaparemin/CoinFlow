using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class MainPage : ContentPage
{
    private static bool _reviewPromptHandled;
    private readonly DashboardViewModel _viewModel;

    public MainPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (_viewModel.HasPendingReview && !_reviewPromptHandled)
        {
            _reviewPromptHandled = true;
            var start = await DisplayAlert(
                "Geçen dönemi güncelleyelim mi?",
                "Bu dönem için bir plan oluşturmuştuk. Ödemelerin ve dönem harcamaların netleştiyse gerçekte ne olduğunu kaydedebiliriz.",
                "Hadi Kaydedelim",
                "Daha Sonra");
            if (start)
            {
                await _viewModel.OpenPeriodReviewAsync();
            }
        }
    }
}
