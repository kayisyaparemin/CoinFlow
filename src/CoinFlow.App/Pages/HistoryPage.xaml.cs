using CoinFlow.App.Models;
using CoinFlow.App.ViewModels;

namespace CoinFlow.App.Pages;

public partial class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;
    private readonly IServiceProvider _services;

    public HistoryPage(
        HistoryViewModel viewModel,
        IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not
            HistoryCardItem selected)
        {
            return;
        }

        ((CollectionView)sender!).SelectedItem = null;
        var page = _services.GetRequiredService<HistoryDetailPage>();
        await page.LoadAsync(selected.ActualId);
        await Navigation.PushModalAsync(new NavigationPage(page));
    }
}
