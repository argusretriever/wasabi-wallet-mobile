using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WalletWasabi.Mobile;

public partial class WalletUnlockView : UserControl
{
    private readonly ProtectedWalletStore _store;
    private readonly Action<TestnetWalletSession> _open;
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IBiometricUnlock? _biometrics = App.BiometricUnlock;

    public WalletUnlockView(ProtectedWalletStore store, Action<TestnetWalletSession> open)
    {
        InitializeComponent();
        _store = store;
        _open = open;
        RefreshBiometrics();
    }

    private async void UnlockWallet(object? sender, RoutedEventArgs args)
    {
        if (_busy) return;
        _busy = true;
        SetButtons(false);
        UnlockStatus.Text = "Unlocking on this device…";
        var password = UnlockPassword.Text ?? "";
        UnlockPassword.Text = "";
        try
        {
            var session = EnableBiometrics.IsChecked == true && _biometrics is { IsAvailable: true }
                ? await new BiometricWalletAccess(_store, _biometrics).EnableAndUnlockAsync(password, _lifetime.Token)
                : await _store.UnlockAsync(password);
            _lifetime.Token.ThrowIfCancellationRequested();
            _open(session);
        }
        catch (OperationCanceledException)
        {
            EnableBiometrics.IsChecked = false;
            UnlockStatus.Text = "Cancelled. Password unlock is available.";
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Biometric/password unlock: {ex}");
#endif
            UnlockStatus.Text = "Could not unlock or enable biometrics. Check your local password, or retry without enabling biometrics. Saved wallet data was not replaced.";
        }
        finally { _busy = false; SetButtons(true); RefreshBiometrics(); }
    }

    private async void UnlockWithBiometrics(object? sender, RoutedEventArgs args)
    {
        if (_busy || _biometrics is null) return;
        _busy = true;
        SetButtons(false);
        UnlockPassword.Text = "";
        try
        {
            var session = await new BiometricWalletAccess(_store, _biometrics).UnlockAsync(_lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _open(session);
        }
        catch (OperationCanceledException) { UnlockStatus.Text = "Cancelled. Enter your password to unlock."; }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Biometric unlock: {ex}");
#endif
            UnlockStatus.Text = "Biometric unlock unavailable. Use your password. You can remove and enable biometrics again after verification.";
        }
        finally { _busy = false; SetButtons(true); RefreshBiometrics(); }
    }

    private void DisableBiometrics(object? sender, RoutedEventArgs args)
    {
        if (_busy) return;
        try { _biometrics?.Disable(); UnlockStatus.Text = "Biometric unlock removed. Password unlock remains available."; }
        catch (Exception) { UnlockStatus.Text = "Could not remove biometric unlock. Use your password."; }
        RefreshBiometrics();
    }

    private void SetButtons(bool enabled) =>
        UnlockButton.IsEnabled = BiometricButton.IsEnabled = DisableBiometricButton.IsEnabled = EnableBiometrics.IsEnabled = enabled;

    private void RefreshBiometrics()
    {
        EnableBiometrics.IsVisible = _biometrics?.IsAvailable == true;
        BiometricButton.IsVisible = _biometrics is { IsEnabled: true, IsAvailable: true };
        DisableBiometricButton.IsVisible = _biometrics?.IsEnabled == true;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs args)
    {
        _lifetime.Cancel();
        UnlockPassword.Text = "";
        base.OnDetachedFromVisualTree(args);
    }
}
