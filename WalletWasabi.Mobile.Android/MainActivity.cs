using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Android.OS;
using Android.Views;

namespace WalletWasabi.Mobile.Android;

[Activity(Label = "Wasabi Mobile Preview",
    Theme = "@style/WasabiTheme",
    Icon = "@drawable/wasabi_logo",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.Secure);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
