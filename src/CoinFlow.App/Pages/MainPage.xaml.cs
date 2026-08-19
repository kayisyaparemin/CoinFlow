using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class MainPage : ContentPage
{
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
    }
}
