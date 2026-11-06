using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WalletWasabi.Mobile;

public partial class WalletUnlockView : UserControl
{
    private readonly ProtectedWalletStore _store;
    private readonly Action<TestnetWalletSession> _open;
    private bool _busy;

    public WalletUnlockView(ProtectedWalletStore store, Action<TestnetWalletSession> open)
    {
        InitializeComponent();
        _store = store;
        _open = open;
    }

    private async void UnlockWallet(object? sender, RoutedEventArgs args)
    {
        if (_busy) return;
        _busy = true;
        UnlockButton.IsEnabled = false;
        UnlockStatus.Text = "Unlocking on this device…";
        var password = UnlockPassword.Text ?? "";
        UnlockPassword.Text = "";
        try { _open(await _store.UnlockAsync(password)); }
        catch (Exception)
        {
            UnlockStatus.Text = "Could not unlock. Check your exact password. Damaged storage or unavailable device keys also require recovery on a fresh installation; your saved wallet has not been replaced.";
        }
        finally { _busy = false; UnlockButton.IsEnabled = true; }
    }
}
