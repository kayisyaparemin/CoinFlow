using CommunityToolkit.Mvvm.ComponentModel;
using CoinFlow.App.Services;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;

namespace CoinFlow.App.ViewModels;

public partial class SalaryPeriodDetailViewModel(
    SalaryPeriodDetailPresenter presenter) :
    ViewModelBase,
    IQueryAttributable
{
    public const string DetailQueryKey = "periodDetail";

    [ObservableProperty] private SalaryPeriodDetailData? detail;
    [ObservableProperty] private bool hasDetail;
    public bool IsDevelopment => BuildInfo.IsDevelopment;

    public void ApplyQueryAttributes(
        IDictionary<string, object> query)
    {
        try
        {
            if (!query.TryGetValue(DetailQueryKey, out var value) ||
                value is not SalaryPeriodDetailRequest request)
            {
                throw new InvalidOperationException(
                    "Dönem detayı bulunamadı.");
            }

            Detail = presenter.Build(
                request.Scenario,
                request.Baseline,
                request.IsSimulationScenario);
            HasDetail = true;
            SetStatus(string.Empty);
        }
        catch (Exception exception)
        {
            Detail = null;
            HasDetail = false;
            SetStatus(exception.Message);
        }
    }
}
