using CoinFlow.App.Services;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Models;

namespace CoinFlow.App.Pages;

public partial class SimulationPage : ContentPage
{
    private readonly SimulationViewModel _viewModel;
    private readonly IUserFeedbackService _feedback;

    public SimulationPage(
        SimulationViewModel viewModel,
        IUserFeedbackService feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.ConsumeDetailReturn())
        {
            await _viewModel.LoadAsync();
        }
    }

    private async void OnApplyPlanClicked(object? sender, EventArgs eventArgs)
    {
        var confirmed = await _feedback.ConfirmAsync(
            "Planı Uygula",
            _viewModel.ApplyConfirmationText,
            "Planı Uygula",
            "Vazgeç");
        if (!confirmed)
        {
            return;
        }

        var result = await _viewModel.ApplyLastPlanAsync();
        if (result is not null)
        {
            var showRecord = await _feedback.ConfirmAsync(
                "Plan uygulandı",
                result.Message,
                result.Destination == SimulationApplyDestination.Settings
                    ? "Ayarlarda Gör"
                    : "Gelir & Ödemelerde Gör",
                "Tamam");
            if (showRecord)
            {
                await NavigateToAppliedRecordAsync(result);
            }
        }
    }

    private static Task NavigateToAppliedRecordAsync(
        SimulationApplyResult result) => result.Destination switch
        {
            SimulationApplyDestination.CreditCard =>
                Shell.Current.GoToAsync(
                    $"//commitments/commitments-content?section=payment&cardId={result.EntityId}"),
            SimulationApplyDestination.Payments =>
                Shell.Current.GoToAsync(
                    "//commitments/commitments-content?section=payment"),
            SimulationApplyDestination.Income or
                SimulationApplyDestination.SalaryHistory =>
                Shell.Current.GoToAsync(
                    "//commitments/commitments-content?section=income"),
            SimulationApplyDestination.Settings =>
                Shell.Current.GoToAsync("//settings/settings-content"),
            _ => throw new ArgumentOutOfRangeException()
        };
}
