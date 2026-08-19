using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class CommitmentsPage : ContentPage
{
    private readonly CommitmentsViewModel _viewModel;

    public CommitmentsPage(CommitmentsViewModel viewModel)
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
