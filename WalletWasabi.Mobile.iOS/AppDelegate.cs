using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace WalletWasabi.Mobile.iOS;

[Register("WasabiWalletAppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    private WalletAvaloniaHost? _host;

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var host = new WalletAvaloniaHost();
        _host = host;
        var launched = host.FinishedLaunching(application, launchOptions!);
        Window = host.Window;
        return launched;
    }

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options) =>
        _host?.OpenUrl(application, url, options) ?? false;

    public override void DidEnterBackground(UIApplication application)
    {
        if (Application.Current is App app) app.OnBackgrounded();
    }

    public override void WillEnterForeground(UIApplication application)
    {
        if (Application.Current is App app) app.OnForegrounded();
    }

    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));

    private sealed class WalletAvaloniaHost : AvaloniaAppDelegate<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var storage = new IosWalletStorage();
            App.DeviceStorage = storage;
            App.BiometricUnlock = new IosBiometricUnlock(storage);
            return base.CustomizeAppBuilder(builder).WithInterFont();
        }
    }
}
