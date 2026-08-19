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
}
