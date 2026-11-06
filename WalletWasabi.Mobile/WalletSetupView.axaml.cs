using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WalletWasabi.Mobile;

public partial class WalletSetupView : UserControl
{
    private readonly Action<TestnetWalletSession> _openWallet;
    private TestnetWalletSession? _createdSession;
    private bool _busy;

    public WalletSetupView(Action<TestnetWalletSession> openWallet)
    {
        InitializeComponent();
        _openWallet = openWallet;
    }

    private async void CreateWallet(object? sender, RoutedEventArgs args)
    {
        if (!ValidatePassword()) return;
        if (!string.IsNullOrWhiteSpace(RecoveryInput.Text))
        {
            SetupStatus.Text = "Use Recover to open the wallet for the entered phrase.";
            return;
        }

        SetBusy(true);
        var password = PasswordInput.Text ?? "";
        try
        {
            var result = await Task.Run(() => TestnetWalletSession.Create(password));
            _createdSession = result.Session;
            RecoveryDisplay.Text = result.RecoveryWords;
            ClearInputs();
            SetupForm.IsVisible = false;
            BackupPanel.IsVisible = true;
            SetupStatus.Text = "Wallet created locally. Save the recovery information before continuing.";
        }
        catch (Exception)
        {
            SetupStatus.Text = "Wallet creation failed on this device. No wallet was saved.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RecoverWallet(object? sender, RoutedEventArgs args)
    {
        if (!ValidatePassword()) return;
        if (string.IsNullOrWhiteSpace(RecoveryInput.Text))
        {
            SetupStatus.Text = "Enter your recovery phrase first.";
            return;
        }

        SetBusy(true);
        var words = RecoveryInput.Text;
        var password = PasswordInput.Text ?? "";
        try
        {
            var session = await Task.Run(() => TestnetWalletSession.Recover(words, password));
            ClearInputs();
            _openWallet(session);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            SetupStatus.Text = "Check the English recovery words, their order and checksum.";
        }
        catch (Exception)
        {
            SetupStatus.Text = "Recovery failed on this device. No wallet was saved.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool ValidatePassword()
    {
        if (_busy) return false;
        if ((PasswordInput.Text ?? "") != (ConfirmInput.Text ?? ""))
        {
            SetupStatus.Text = "The passwords must match exactly.";
            return false;
        }

        return true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SetupForm.IsEnabled = !busy;
        if (busy) SetupStatus.Text = "Deriving wallet keys on this device…";
    }

    private void ClearInputs()
    {
        RecoveryInput.Text = "";
        PasswordInput.Text = "";
        ConfirmInput.Text = "";
    }

    private void BackupConfirmationChanged(object? sender, RoutedEventArgs args) =>
        OpenButton.IsEnabled = BackupConfirmed.IsChecked == true;

    private void OpenWallet(object? sender, RoutedEventArgs args)
    {
        if (_createdSession is not { } session || BackupConfirmed.IsChecked != true) return;
        RecoveryDisplay.Text = "";
        _createdSession = null;
        _openWallet(session);
    }
}
