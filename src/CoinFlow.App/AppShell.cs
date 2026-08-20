using CoinFlow.App.Pages;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, true);

        var tabs = new TabBar
        {
            Route = "main"
        };
        tabs.Items.Add(CreateTab(
            "Ana Sayfa",
            "home",
            "dashboard-content",
            () => services.GetRequiredService<MainPage>()));
        tabs.Items.Add(CreateTab(
            "12 Aylık",
            "projection",
            "future-months-content",
            () => services.GetRequiredService<FutureMonthsPage>()));
        tabs.Items.Add(CreateTab(
            "Simülatör",
            "simulation",
            "simulation-content",
            () => services.GetRequiredService<SimulationPage>()));
        tabs.Items.Add(CreateTab(
            "Gelir & Ödemeler",
            "income-payments",
            "commitments-content",
            () => services.GetRequiredService<CommitmentsPage>()));
        Items.Add(tabs);

        Routing.RegisterRoute(
            "settings",
            typeof(SettingsPage));
    }

    private static Tab CreateTab(
        string title,
        string route,
        string contentRoute,
        Func<Page> factory)
    {
        var tab = new Tab
        {
            Title = title,
            Route = route
        };
        tab.Items.Add(new ShellContent
        {
            Title = title,
            Route = contentRoute,
            ContentTemplate = new DataTemplate(factory)
        });
        return tab;
    }
}
