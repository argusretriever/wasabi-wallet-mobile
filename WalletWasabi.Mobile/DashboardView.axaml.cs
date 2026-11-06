using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.VisualTree;

namespace WalletWasabi.Mobile;

public partial class DashboardView : UserControl
{
    private readonly TestnetWalletSession _session;
    private readonly Action _closeWallet;
    private CancellationTokenSource? _syncCancellation;
    private Task? _syncTask;
    private Task? _lockTask;

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

    private async void StartSync(object? sender, RoutedEventArgs args)
    {
        if (_syncTask is not null) return;
        StartSyncButton.IsEnabled = false;
        StopSyncButton.IsEnabled = true;
        var cancellation = new CancellationTokenSource();
        _syncCancellation = cancellation;
        var progress = new Progress<WalletSyncSnapshot>(snapshot =>
        {
            if (ReferenceEquals(_syncCancellation, cancellation)) UpdateSync(snapshot);
        });
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WasabiMobile", "Testnet4");
        _syncTask = Task.Run(() => new TestnetWalletSync().RunAsync(
            _session, path, progress, cancellation.Token));
        try
        {
            await _syncTask;
        }
        catch (OperationCanceledException)
        {
            SyncStatus.Text = "Sync stopped.";
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine(ex);
#endif
            SyncStatus.Text = "Sync could not start or continue. Check the local Tor service on port 9050, then retry.";
        }
        finally
        {
            _syncCancellation.Dispose();
            _syncCancellation = null;
            _syncTask = null;
            StartSyncButton.IsEnabled = true;
            StopSyncButton.IsEnabled = false;
            BalanceDisplay.Text = "Not synchronized";
        }
    }

    private void UpdateSync(WalletSyncSnapshot snapshot)
    {
        if (_syncCancellation is not { IsCancellationRequested: false }) return;
        SyncStatus.Text = snapshot.Status;
        SyncProgress.Text = $"Peers {snapshot.PeerCount} · scanned {snapshot.ScannedHeight} / network {snapshot.NetworkHeight}";
        BalanceDisplay.Text = snapshot.BalanceSatoshis is { } sats
            ? (sats / 100_000_000m).ToString("0.########", CultureInfo.InvariantCulture) + " BTC"
            : "Not synchronized";
        TransactionsDisplay.Text = snapshot.BalanceSatoshis is null
            ? "Waiting for a completed wallet scan."
            : snapshot.Transactions.Length == 0 ? "No transactions found."
            : string.Join(Environment.NewLine + Environment.NewLine, snapshot.Transactions);
    }

    private void StopSync(object? sender, RoutedEventArgs args) => RequestStop();

    private void RequestStop()
    {
        if (_syncCancellation is null) return;
        SyncStatus.Text = "Stopping sync…";
        StopSyncButton.IsEnabled = false;
        _syncCancellation.Cancel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        if (Application.Current is App app) app.Backgrounded += RequestStop;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        if (Application.Current is App app) app.Backgrounded -= RequestStop;
        RequestStop();
        base.OnDetachedFromVisualTree(args);
    }

    private async void CloseWallet(object? sender, RoutedEventArgs args)
    {
        await LockAsync();
    }

    public Task LockAsync() => _lockTask ??= LockCoreAsync();

    private async Task LockCoreAsync()
    {
        CloseWalletButton.IsEnabled = false;
        RequestStop();
        if (_syncTask is { } task)
        {
            try { await task; }
            catch (Exception) { /* The sync operation reports its failure in StartSync. */ }
        }
        _closeWallet();
    }
}
