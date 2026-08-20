using CoinFlow.App.Pages;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Flyout;
        Shell.SetNavBarIsVisible(this, true);

        Items.Add(CreateFlyoutItem(
            "Ana Sayfa",
            "dashboard",
            "dashboard-content",
            () => services.GetRequiredService<MainPage>()));
        Items.Add(CreateFlyoutItem(
            "12 Aylık",
            "projection",
            "future-months-content",
            () => services.GetRequiredService<FutureMonthsPage>()));
        Items.Add(CreateFlyoutItem(
            "Simülatör",
            "simulation",
            "simulation-content",
            () => services.GetRequiredService<SimulationPage>()));
        Items.Add(CreateFlyoutItem(
            "Gelir & Ödemeler",
            "commitments",
            "commitments-content",
            () => services.GetRequiredService<CommitmentsPage>()));
        Items.Add(CreateFlyoutItem(
            "Ayarlar",
            "settings",
            "settings-content",
            () => services.GetRequiredService<SettingsPage>()));
    }

    private static FlyoutItem CreateFlyoutItem(
        string title,
        string route,
        string contentRoute,
        Func<Page> factory)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Route = contentRoute,
            ContentTemplate = new DataTemplate(factory)
        });
        return item;
    }
}
