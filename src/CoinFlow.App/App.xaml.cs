namespace CoinFlow.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        MainPage = shell;
    }
}
