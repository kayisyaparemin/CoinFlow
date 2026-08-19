using CoinFlow.App.Pages;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutHeaderBehavior = FlyoutHeaderBehavior.CollapseOnScroll;
        Shell.SetNavBarIsVisible(this, true);

        FlyoutHeader = CreateFlyoutHeader();

        Items.Add(CreateFlyoutItem("Özet", "dashboard", () => services.GetRequiredService<MainPage>()));
        Items.Add(CreateFlyoutItem("Harcama ekle", "expense", () => services.GetRequiredService<ExpensePage>()));
        Items.Add(CreateFlyoutItem("Planlar ve borçlar", "commitments", () => services.GetRequiredService<CommitmentsPage>()));
        Items.Add(CreateFlyoutItem("Önündeki 12 ay", "future-months", () => services.GetRequiredService<FutureMonthsPage>()));
        Items.Add(CreateFlyoutItem("Simülasyon", "simulation", () => services.GetRequiredService<SimulationPage>()));
        Items.Add(CreateFlyoutItem("Ayarlar", "settings", () => services.GetRequiredService<SettingsPage>()));
    }

    private static FlyoutItem CreateFlyoutItem(string title, string route, Func<Page> factory)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route,
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem
        };

        item.Items.Add(new ShellContent
        {
            Title = title,
            Route = $"{route}-content",
            ContentTemplate = new DataTemplate(factory)
        });

        return item;
    }

    private static View CreateFlyoutHeader()
    {
        var title = new Label
        {
            Text = "CoinFlow",
            FontFamily = "OpenSansSemibold",
            FontSize = 26
        };
        title.SetDynamicResource(Label.TextColorProperty, "Ink");

        var subtitle = new Label
        {
            Text = "Paranın ritmi sende",
            FontFamily = "OpenSansRegular",
            FontSize = 13
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "Muted");

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(22, 36, 22, 20),
            Spacing = 3,
            Children = { title, subtitle }
        };
        content.SetDynamicResource(VisualElement.BackgroundColorProperty, "FlyoutHeaderSurface");

        return content;
    }
}
