using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Foundation;
using UIKit;

namespace WalletWasabi.Mobile.iOS;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate, IAvaloniaAppDelegate
{
    public override UIWindow? Window { get; set; }
    public event EventHandler<ActivatedEventArgs>? Activated;
    public event EventHandler<ActivatedEventArgs>? Deactivated;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        App.DeviceStorage = new IosWalletStorage();
        var lifetime = new WalletViewLifetime();
        AppBuilder.Configure<App>().UseiOS(this).WithInterFont()
            .AfterApplicationSetup(_ =>
            {
                var view = new AvaloniaView { Content = lifetime.MainView };
                var controller = new DefaultAvaloniaViewController { View = view };
                view.InitWithController(controller);
                lifetime.Host = view;
                Window = new UIWindow { RootViewController = controller };
            })
            .SetupWithLifetime(lifetime);
        Window!.MakeKeyAndVisible();
        return true;
    }

    public override void DidEnterBackground(UIApplication application)
    {
        if (Application.Current is App app) app.OnBackgrounded();
        Deactivated?.Invoke(this, new ActivatedEventArgs(ActivationKind.Background));
    }

    public override void WillEnterForeground(UIApplication application)
    {
        if (Application.Current is App app) app.OnForegrounded();
        Activated?.Invoke(this, new ActivatedEventArgs(ActivationKind.Background));
    }

    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));

    private sealed class WalletViewLifetime : ISingleViewApplicationLifetime
    {
        private Control? _mainView;
        public AvaloniaView? Host { get; set; }

        public Control? MainView
        {
            get => _mainView;
            set
            {
                _mainView = value;
                if (Host is { } view) view.Content = value;
            }
        }
    }
}
