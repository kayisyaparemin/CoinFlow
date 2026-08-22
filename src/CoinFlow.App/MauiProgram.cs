using CoinFlow.App.Pages;
using CoinFlow.App.ViewModels;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace CoinFlow.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<IClock, SystemClock>();
#if COINFLOW_DEV_BUILD
        const bool developmentFeaturesEnabled = true;
#else
        const bool developmentFeaturesEnabled = false;
#endif
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "coinflow.db3");
        builder.Services.AddSingleton<ICoinFlowStore>(
            services => new SqliteCoinFlowStore(
                databasePath,
                developmentFeaturesEnabled,
                services.GetRequiredService<IClock>().Today));
        builder.Services.AddSingleton<SalaryPeriodCalculator>();
        builder.Services.AddSingleton<PaymentAssignmentStrategyResolver>();
        builder.Services.AddSingleton<SalaryFundingPlanner>();
        builder.Services.AddSingleton<SalaryResolver>();
        builder.Services.AddSingleton<IncomeProjectionCalculator>();
        builder.Services.AddSingleton<LoanScheduleCalculator>();
        builder.Services.AddSingleton<InstallmentScheduleCalculator>();
        builder.Services.AddSingleton<ScheduledPaymentCalculator>();
        builder.Services.AddSingleton<CreditCardStatementCalculator>();
        builder.Services.AddSingleton<CreditCardActualPaymentReconciler>();
        builder.Services.AddSingleton<MandatoryPaymentCalculator>();
        builder.Services.AddSingleton<FinancialProjectionCalculator>();
        builder.Services.AddSingleton<FinancialProjectionService>();
        builder.Services.AddSingleton<PeriodPlanSnapshotService>();
        builder.Services.AddSingleton<FinancialSnapshotService>();
        builder.Services.AddSingleton<HistoricalPlanRevisionService>();
        builder.Services.AddSingleton<FinancialStateReconciliationService>();
        builder.Services.AddSingleton<FinancialInstrumentReconciliationService>();
        builder.Services.AddSingleton<PlanActualComparisonCalculator>();
        builder.Services.AddSingleton<PeriodReviewService>();
        builder.Services.AddSingleton<HistoryQueryService>();
        builder.Services.AddSingleton<SalaryPeriodDetailPresenter>();
        builder.Services.AddSingleton<SimulationCalculator>();
        builder.Services.AddSingleton<TargetAmountCalculator>();
        builder.Services.AddSingleton<CoinFlowService>();

        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<CommitmentsViewModel>();
        builder.Services.AddTransient<FutureMonthsViewModel>();
        builder.Services.AddTransient<SimulationViewModel>();
        builder.Services.AddTransient<SalaryPeriodDetailViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<PeriodReviewWizardViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();
        builder.Services.AddTransient<HistoryDetailViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<CommitmentsPage>();
        builder.Services.AddTransient<FutureMonthsPage>();
        builder.Services.AddTransient<SimulationPage>();
        builder.Services.AddTransient<SalaryPeriodDetailPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<PeriodReviewPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<HistoryDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
