using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace WalletWasabi.Mobile;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime mobile)
        {
            ShowSetup(mobile);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowSetup(ISingleViewApplicationLifetime mobile)
    {
        mobile.MainView = new WalletSetupView(session =>
            mobile.MainView = new DashboardView(session, () => ShowSetup(mobile)));
    }
}
