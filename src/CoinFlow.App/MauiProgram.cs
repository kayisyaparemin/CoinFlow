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
        const bool seedDevelopmentData = true;
#else
        const bool seedDevelopmentData = false;
#endif
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "coinflow.db3");
        builder.Services.AddSingleton<ICoinFlowStore>(
            _ => new SqliteCoinFlowStore(databasePath, seedDevelopmentData));
        builder.Services.AddSingleton<SalaryPeriodCalculator>();
        builder.Services.AddSingleton<DailyCoinCalculator>();
        builder.Services.AddSingleton<CreditCardProjectionCalculator>();
        builder.Services.AddSingleton<PurchaseSimulationCalculator>();
        builder.Services.AddSingleton<CoinFlowService>();

        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<ExpenseViewModel>();
        builder.Services.AddTransient<CommitmentsViewModel>();
        builder.Services.AddTransient<FutureMonthsViewModel>();
        builder.Services.AddTransient<SimulationViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<ExpensePage>();
        builder.Services.AddTransient<CommitmentsPage>();
        builder.Services.AddTransient<FutureMonthsPage>();
        builder.Services.AddTransient<SimulationPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
