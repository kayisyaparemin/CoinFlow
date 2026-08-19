using CoinFlow.App.Pages;

namespace CoinFlow.App;

public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        FlyoutBehavior = FlyoutBehavior.Flyout;
        Shell.SetNavBarIsVisible(this, true);

        var menuItems = new[]
        {
            (Title: "Özet", ColorKey: "SoftPink", Item: CreateFlyoutItem("Özet", "dashboard", () => services.GetRequiredService<MainPage>())),
            (Title: "Harcama ekle", ColorKey: "SoftPeach", Item: CreateFlyoutItem("Harcama ekle", "expense", () => services.GetRequiredService<ExpensePage>())),
            (Title: "Ödemeler", ColorKey: "SoftYellow", Item: CreateFlyoutItem("Ödemeler", "commitments", () => services.GetRequiredService<CommitmentsPage>())),
            (Title: "12 maaş dönemi", ColorKey: "SoftSky", Item: CreateFlyoutItem("12 maaş dönemi", "future-months", () => services.GetRequiredService<FutureMonthsPage>())),
            (Title: "Simülasyon", ColorKey: "SoftLavender", Item: CreateFlyoutItem("Simülasyon", "simulation", () => services.GetRequiredService<SimulationPage>())),
            (Title: "Ayarlar", ColorKey: "SoftPink", Item: CreateFlyoutItem("Ayarlar", "settings", () => services.GetRequiredService<SettingsPage>()))
        };

        foreach (var menuItem in menuItems)
        {
            Items.Add(menuItem.Item);
        }

        FlyoutContent = CreateFlyoutContent(menuItems);
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

    private View CreateFlyoutContent(
        IEnumerable<(string Title, string ColorKey, FlyoutItem Item)> menuItems)
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

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(22, 34, 22, 22),
            Spacing = 3,
            Children = { title, subtitle }
        };
        header.SetDynamicResource(VisualElement.BackgroundColorProperty, "SoftPink");

        var menu = new VerticalStackLayout
        {
            Padding = new Thickness(18, 20),
            Spacing = 10
        };

        foreach (var menuItem in menuItems)
        {
            var button = new Button
            {
                Text = menuItem.Title,
                FontFamily = "OpenSansSemibold",
                FontSize = 15,
                CornerRadius = 16,
                MinimumHeightRequest = 54,
                HorizontalOptions = LayoutOptions.Fill
            };
            button.SetDynamicResource(Button.BackgroundColorProperty, menuItem.ColorKey);
            button.SetDynamicResource(Button.TextColorProperty, "Ink");
            button.Clicked += (_, _) =>
            {
                CurrentItem = menuItem.Item;
                FlyoutIsPresented = false;
            };
            menu.Children.Add(button);
        }

        var offlineNote = new Label
        {
            Text = "Tamamen çevrimdışı • Veriler cihazında",
            FontFamily = "OpenSansRegular",
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        offlineNote.SetDynamicResource(Label.TextColorProperty, "Muted");
        menu.Children.Add(offlineNote);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "SoftYellow");
        root.Children.Add(header);

        var scrollView = new ScrollView { Content = menu };
        Grid.SetRow(scrollView, 1);
        root.Children.Add(scrollView);

        return root;
    }
}
