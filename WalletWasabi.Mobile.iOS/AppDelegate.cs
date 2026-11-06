using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace WalletWasabi.Mobile.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.DeviceStorage = new IosWalletStorage();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    public static void Main(string[] args) =>
        UIApplication.Main(args, null, typeof(AppDelegate));

    public override void DidEnterBackground(UIApplication application)
    {
        if (Avalonia.Application.Current is App app) app.OnBackgrounded();
        base.DidEnterBackground(application);
    }

    public override void WillEnterForeground(UIApplication application)
    {
        base.WillEnterForeground(application);
        if (Avalonia.Application.Current is App app) app.OnForegrounded();
    }
}
