using CoinFlow.Application.Models;
using CoinFlow.App.ViewModels;
using CoinFlow.Domain.Models;
using System.Globalization;

namespace CoinFlow.App.Pages;

public partial class InitialStrategyPage : ContentPage
{
    private readonly CommitmentsViewModel _viewModel;
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public InitialStrategyPage(
        InitialPaymentStrategySetup setup,
        CommitmentsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        PreviousDescription =
            $"{DateText(setup.ExampleSalaryDate)} maaşıyla " +
            $"{DateText(setup.PreviousExampleStart)}–" +
            $"{DateText(setup.ExampleSalaryDate)} dönemindeki ödemeleri kapatırım.";
        UpcomingDescription =
            $"{DateText(setup.ExampleSalaryDate)} maaşıyla " +
            $"{DateText(setup.ExampleSalaryDate)}–" +
            $"{DateText(setup.UpcomingExampleEnd)} dönemindeki ödemeleri karşılarım.";
        EffectiveDescription =
            $"İlk kararın {setup.EffectiveSalaryDate:dd.MM.yyyy} maaşından itibaren geçerli olacak. Planlama başlangıcı: {setup.ProjectionAnchorDate:dd.MM.yyyy}.";
        BindingContext = this;
    }

    public Task Completion => _completion.Task;
    public string PreviousDescription { get; }
    public string UpcomingDescription { get; }
    public string EffectiveDescription { get; }

    private void OnOptionCheckedChanged(object? sender, CheckedChangedEventArgs args)
    {
        SaveButton.IsEnabled = PreviousOption.IsChecked ||
                               UpcomingOption.IsChecked;
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        SaveButton.IsEnabled = false;
        var mode = PreviousOption.IsChecked
            ? PaymentAssignmentMode.PreviousPeriod
            : PaymentAssignmentMode.UpcomingPeriod;
        if (await _viewModel.CompleteInitialStrategySetupAsync(mode))
        {
            _completion.TrySetResult();
            await Navigation.PopModalAsync();
            return;
        }

        SaveButton.IsEnabled = true;
        await DisplayAlert(
            "Düzen kaydedilemedi",
            StatusMessage(),
            "Tamam");
    }

    protected override bool OnBackButtonPressed()
    {
        return true;
    }

    private static string DateText(DateOnly date) =>
        date.ToString("d MMMM", CultureInfo.GetCultureInfo("tr-TR"));

    private string StatusMessage() =>
        string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
            ? "Lütfen yeniden dene."
            : _viewModel.StatusMessage;
}
