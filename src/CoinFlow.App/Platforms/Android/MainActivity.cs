using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace CoinFlow.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Window is null)
        {
            return;
        }

        Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#FFCABF"));
        Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#FFFACF"));

        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        controller.AppearanceLightStatusBars = true;
        controller.AppearanceLightNavigationBars = true;
    }
}
