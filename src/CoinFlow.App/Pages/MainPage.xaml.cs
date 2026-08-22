using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class MainPage : ContentPage
{
    private static bool _reviewPromptHandled;
    private readonly DashboardViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public MainPage(
        DashboardViewModel viewModel,
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
        if (_viewModel.HasPendingReview && !_reviewPromptHandled)
        {
            _reviewPromptHandled = true;
            var start = await _feedback.ConfirmAsync(
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
