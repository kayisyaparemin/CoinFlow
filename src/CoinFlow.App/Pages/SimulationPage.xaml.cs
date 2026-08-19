using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class SimulationPage : ContentPage
{
    public SimulationPage(SimulationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
