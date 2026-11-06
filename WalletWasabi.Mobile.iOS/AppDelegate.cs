using Avalonia;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace WalletWasabi.Mobile.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();

    public static void Main(string[] args) =>
        UIApplication.Main(args, null, typeof(AppDelegate));
}
