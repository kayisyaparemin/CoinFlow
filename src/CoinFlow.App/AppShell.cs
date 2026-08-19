using CoinFlow.App.Pages;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, false);

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Özet", () => services.GetRequiredService<MainPage>()));
        tabs.Items.Add(CreateTab("Harcama", () => services.GetRequiredService<ExpensePage>()));
        tabs.Items.Add(CreateTab("Planlar", () => services.GetRequiredService<CommitmentsPage>()));
        tabs.Items.Add(CreateTab("12 Ay", () => services.GetRequiredService<FutureMonthsPage>()));
        tabs.Items.Add(CreateTab("Simülasyon", () => services.GetRequiredService<SimulationPage>()));
        tabs.Items.Add(CreateTab("Ayarlar", () => services.GetRequiredService<SettingsPage>()));
        Items.Add(tabs);
    }

    private static Tab CreateTab(string title, Func<Page> factory)
    {
        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent { Title = title, ContentTemplate = new DataTemplate(factory) });
        return tab;
    }
}
