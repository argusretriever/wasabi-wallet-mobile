using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace WalletWasabi.Mobile;

public partial class DashboardView : UserControl
{
    private readonly TestnetWalletSession _session;
    private readonly Action _closeWallet;

    public DashboardView(TestnetWalletSession session, Action closeWallet)
    {
        InitializeComponent();
        _session = session;
        _closeWallet = closeWallet;
        AddressDisplay.Text = session.ReceivingAddress;
        DerivationDisplay.Text = $"Key path {session.ReceivingKeyPath}";
        FingerprintDisplay.Text = $"Master fingerprint {session.Fingerprint}";
    }

    private void ShowSendStatus(object? sender, RoutedEventArgs args)
    {
        ActionStatus.Text = "Transaction building, signing and network broadcast are pending.";
    }

    private void ShowReceiveStatus(object? sender, RoutedEventArgs args)
    {
        ActionStatus.Text = $"Testnet4 receiving address: {_session.ReceivingAddress}";
    }

    private void CloseWallet(object? sender, RoutedEventArgs args) => _closeWallet();
}
