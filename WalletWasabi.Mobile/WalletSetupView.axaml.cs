using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WalletWasabi.Mobile;

public partial class WalletSetupView : UserControl
{
    private readonly Action<TestnetWalletSession> _openWallet;
    private readonly ProtectedWalletStore _store;
    private TestnetWalletSession? _createdSession;
    private string? _createdWords;
    private string? _localUnlockPassword;
    private bool _busy;
    private bool _active = true;

    public WalletSetupView(Action<TestnetWalletSession> openWallet, ProtectedWalletStore store)
    {
        InitializeComponent();
        _openWallet = openWallet;
        _store = store;
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
        var localPassword = LocalPassword.Text ?? "";
        try
        {
            var result = await Task.Run(() => TestnetWalletSession.Create(password));
            if (!_active) return;
            _createdSession = result.Session;
            _createdWords = result.RecoveryWords;
            _localUnlockPassword = localPassword;
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
            if (!_active) return;
            await _store.SaveNewAsync(session, LocalPassword.Text ?? "");
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
        if (string.IsNullOrWhiteSpace(LocalPassword.Text) || LocalPassword.Text.Length < 12 ||
            LocalPassword.Text != LocalConfirm.Text)
        {
            SetupStatus.Text = "Use a local unlock password of at least 12 characters and confirm it exactly.";
            return false;
        }
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
        LocalPassword.Text = LocalConfirm.Text = "";
    }

    private void BackupConfirmationChanged(object? sender, RoutedEventArgs args) =>
        OpenButton.IsEnabled = BackupConfirmed.IsChecked == true;

    private async void OpenWallet(object? sender, RoutedEventArgs args)
    {
        if (_busy || _createdSession is not { } session || BackupConfirmed.IsChecked != true) return;
        if (!VerifyBackup(_createdWords ?? "", WordOne.Text, WordSix.Text, WordTwelve.Text))
        {
            SetupStatus.Text = "Check recovery words 1, 6 and 12 before saving.";
            return;
        }
        _busy = true;
        OpenButton.IsEnabled = false;
        try
        {
            await _store.SaveNewAsync(session, _localUnlockPassword ?? "");
        }
        catch (Exception)
        {
            SetupStatus.Text = "Could not save securely. Keep your recovery words and retry. Existing wallet data has not been replaced.";
            return;
        }
        finally { _busy = false; OpenButton.IsEnabled = true; }
        RecoveryDisplay.Text = "";
        WordOne.Text = WordSix.Text = WordTwelve.Text = "";
        _createdWords = null;
        _localUnlockPassword = null;
        _createdSession = null;
        _openWallet(session);
    }

    public static bool VerifyBackup(string words, string? first, string? sixth, string? twelfth)
    {
        var parts = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 12 && parts[0] == first?.Trim() &&
            parts[5] == sixth?.Trim() && parts[11] == twelfth?.Trim();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    {
        _active = false;
        ClearInputs();
        RecoveryDisplay.Text = "";
        WordOne.Text = WordSix.Text = WordTwelve.Text = "";
        _createdSession = null;
        _createdWords = _localUnlockPassword = null;
        base.OnDetachedFromVisualTree(args);
    }
}
