using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class HistoryDetailPage : ContentPage
{
    private readonly HistoryDetailViewModel _viewModel;

    public HistoryDetailPage(HistoryDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync(Guid actualId) =>
        _viewModel.LoadAsync(actualId);

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
