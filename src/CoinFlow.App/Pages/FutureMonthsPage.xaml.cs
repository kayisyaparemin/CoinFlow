using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class FutureMonthsPage : ContentPage
{
    private readonly FutureMonthsViewModel _viewModel;

    public FutureMonthsPage(FutureMonthsViewModel viewModel)
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
