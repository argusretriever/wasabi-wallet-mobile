using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace WalletWasabi.Mobile.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    [Export("init")]
    public AppDelegate()
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.DeviceStorage = new IosWalletStorage();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    public static void Main(string[] args)
    {
        using var background = UIApplication.Notifications.ObserveDidEnterBackground((_, _) =>
        {
            if (Application.Current is App app) app.OnBackgrounded();
        });
        using var foreground = UIApplication.Notifications.ObserveWillEnterForeground((_, _) =>
        {
            if (Application.Current is App app) app.OnForegrounded();
        });
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
