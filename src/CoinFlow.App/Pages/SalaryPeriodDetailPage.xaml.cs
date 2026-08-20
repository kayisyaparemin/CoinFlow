using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class SalaryPeriodDetailPage : ContentPage
{
    public SalaryPeriodDetailPage(
        SalaryPeriodDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
