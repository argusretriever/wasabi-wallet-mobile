using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using WalletWasabi.Logging;

namespace WalletWasabi.Mobile;

public class App : Application
{
    public static IDeviceWalletStorage? DeviceStorage { get; set; }
    private ProtectedWalletStore? _store;
    private bool _backgrounded;
    private bool _closing;
    private int _entryGeneration;
    public event Action? Backgrounded;
    public async void OnBackgrounded()
    {
        _backgrounded = true;
        _entryGeneration++;
        Backgrounded?.Invoke();
        if (ApplicationLifetime is not ISingleViewApplicationLifetime mobile) return;
        var dashboard = mobile.MainView as DashboardView;
        mobile.MainView = Message("Wallet locked.");
        if (dashboard is null) return;
        _closing = true;
        try { await dashboard.LockAsync(); }
        finally { _closing = false; ShowEntry(mobile); }
    }

    public void OnForegrounded()
    {
        _backgrounded = false;
        if (_store is not null && ApplicationLifetime is ISingleViewApplicationLifetime mobile && mobile.MainView is not DashboardView)
            ShowEntry(mobile);
    }

    public override void Initialize()
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WasabiMobile", "Logs.txt");
#if DEBUG
        Logger.Configure(logPath, logModes: [LogMode.Debug]);
#else
        Logger.Configure(logPath, logModes: []);
#endif
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
        {
            try
            {
                _store = new ProtectedWalletStore(DeviceStorage ?? throw new InvalidOperationException("Device storage unavailable."));
                ShowEntry(mobile);
            }
            catch (Exception)
            {
                mobile.MainView = new Avalonia.Controls.TextBlock
                {
                    Text = "Protected wallet storage is unavailable. Unlock your device and restart the app. Existing data has not been replaced.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(24)
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Avalonia.Controls.TextBlock Message(string text) => new()
    {
        Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24)
    };

    private void ShowEntry(ISingleViewApplicationLifetime mobile)
    {
        var generation = ++_entryGeneration;
        if (_backgrounded || _closing)
        {
            mobile.MainView = Message("Wallet locked. Closing network services…");
            return;
        }
        void Open(TestnetWalletSession session)
        {
            if (generation != _entryGeneration || _backgrounded || _closing) return;
            mobile.MainView = new DashboardView(session, () => ShowEntry(mobile));
        }
        try
        {
            var store = _store ?? throw new InvalidOperationException();
            mobile.MainView = store.Exists ? new WalletUnlockView(store, Open) : new WalletSetupView(Open, store);
        }
        catch (Exception)
        {
            mobile.MainView = Message("Protected storage is unavailable. Unlock the device and restart the app. Saved data has not been replaced.");
        }
    }
}
