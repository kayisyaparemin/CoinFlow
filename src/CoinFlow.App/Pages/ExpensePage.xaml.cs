using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class ExpensePage : ContentPage
{
    private readonly ExpenseViewModel _viewModel;

    public ExpensePage(ExpenseViewModel viewModel)
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
