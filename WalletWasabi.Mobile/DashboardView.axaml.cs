using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private TestnetWalletSync? _sync;
    private WalletSyncSnapshot? _lastSnapshot;
    private CancellationTokenSource? _paymentCancellation;
    private TestnetPaymentDraft? _paymentDraft;
    private NBitcoin.Transaction? _signedPayment;
    private bool _broadcasting;
    private int _paymentVersion;
    private bool _initializing = true;

    public DashboardView(TestnetWalletSession session, Action closeWallet)
    {
        InitializeComponent();
        _initializing = false;
        _session = session;
        _closeWallet = closeWallet;
        AddressDisplay.Text = session.ReceivingAddress;
        DerivationDisplay.Text = $"Key path {session.ReceivingKeyPath}";
        FingerprintDisplay.Text = $"Master fingerprint {session.Fingerprint}";
    }

    private void ShowSendStatus(object? sender, RoutedEventArgs args)
    {
        PaymentPanel.IsVisible = true;
        ActionStatus.Text = "Prepare an unsigned Testnet4 payment for review.";
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
        var sync = new TestnetWalletSync();
        _sync = sync;
        _syncTask = Task.Run(() => sync.RunAsync(
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
            System.Diagnostics.Debug.WriteLine(ex);
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
            _sync = null;
            _lastSnapshot = null;
            InvalidatePaymentReview();
        }
    }

    private void UpdateSync(WalletSyncSnapshot snapshot)
    {
        if (_syncCancellation is not { IsCancellationRequested: false }) return;
        if (_lastSnapshot?.BalanceSatoshis != snapshot.BalanceSatoshis ||
            _lastSnapshot is not null && !_lastSnapshot.Transactions.SequenceEqual(snapshot.Transactions))
            InvalidatePaymentReview();
        _lastSnapshot = snapshot;
        SyncStatus.Text = snapshot.Status;
        SyncProgress.Text = $"Peers {snapshot.PeerCount} · filter peers {snapshot.FilterPeerCount}\n" +
            $"Network {snapshot.NetworkHeight} · headers {snapshot.BlockHeaderHeight}\n" +
            $"Filter headers {snapshot.FilterHeaderHeight} · filters {snapshot.FilterHeight} · scanned {snapshot.ScannedHeight}";
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
        _lastSnapshot = null;
        InvalidatePaymentReview();
        if (_syncCancellation is null) return;
        SyncStatus.Text = "Stopping sync…";
        StopSyncButton.IsEnabled = false;
        _syncCancellation.Cancel();
    }

    private void PaymentEdited(object? sender, TextChangedEventArgs args)
    {
        if (!_initializing) InvalidatePaymentReview();
    }

    private void InvalidatePaymentReview()
    {
        _paymentVersion++;
        _paymentCancellation?.Cancel();
        _paymentDraft = null;
        _signedPayment = null;
        BroadcastPanel.IsVisible = false;
        BroadcastConsent.IsChecked = false;
        SigningPassphrase.Text = "";
        SigningResult.Text = "";
        SignPaymentButton.IsEnabled = true;
        PaymentReviewPanel.IsVisible = false;
        PaymentReviewText.Text = "";
        PaymentStatus.Text = "";
    }

    private void ClosePayment(object? sender, RoutedEventArgs args)
    {
        InvalidatePaymentReview();
        RecipientInput.Text = PaymentAmountInput.Text = "";
        PaymentStatus.Text = "";
        PaymentPanel.IsVisible = false;
    }

    private async void ReviewPayment(object? sender, RoutedEventArgs args)
    {
        if (_paymentCancellation is not null || _broadcasting) return;
        InvalidatePaymentReview();
        TestnetPaymentRequest request;
        try { request = TestnetPaymentRequest.Parse(RecipientInput.Text ?? "", PaymentAmountInput.Text ?? "", PaymentFeeInput.Text ?? ""); }
        catch (FormatException ex) { PaymentStatus.Text = ex.Message; return; }
        if (_sync is not { } sync || _lastSnapshot?.BalanceSatoshis is null)
        {
            PaymentStatus.Text = "Start sync and wait for a completed connected scan, then build the review.";
            return;
        }
        var version = _paymentVersion;
        var cancellation = new CancellationTokenSource();
        _paymentCancellation = cancellation;
        ReviewPaymentButton.IsEnabled = false;
        PaymentStatus.Text = "Selecting confirmed coins and calculating fee and change…";
        try
        {
            var draft = await sync.BuildPaymentDraftAsync(request, cancellation.Token);
            if (version != _paymentVersion || cancellation.IsCancellationRequested) return;
            _paymentDraft = draft;
            var review = _paymentDraft.Review;
            static string Amount(long sats) => (sats / 100_000_000m).ToString("0.########", CultureInfo.InvariantCulture) + $" BTC ({sats} sat)";
            PaymentReviewText.Text = $"Network: Testnet4\n\nRecipient\n{review.Recipient}\n\nAmount: {Amount(review.AmountSatoshis)}\nFee: {Amount(review.FeeSatoshis)}\nRequested rate: {review.RequestedFeeRate.ToString(CultureInfo.InvariantCulture)} sat/vB\nAmount + fee: {Amount(review.TotalDebitSatoshis)}\nInputs: {review.InputCount}\n\nChange back to your wallet: {Amount(review.ChangeSatoshis)}" +
                string.Concat(review.Change.Select(x => $"\n\n{x.Address}\n{Amount(x.Satoshis)}")) +
                (review.PaysOwnAddress ? "\n\nThe recipient address belongs to this wallet." : "");
            PaymentReviewPanel.IsVisible = true;
            PaymentStatus.Text = review.FeeSatoshis > review.AmountSatoshis / 10m
                ? "Unsigned review ready. The fee exceeds 10% of the recipient amount; check your fee rate."
                : "Unsigned review ready. Nothing has been signed or sent.";
        }
        catch (OperationCanceledException) { PaymentStatus.Text = "Draft cancelled. Rebuild the review after any change."; }
        catch (WalletWasabi.Exceptions.InsufficientBalanceException) { PaymentStatus.Text = "Not enough confirmed funds for the amount plus fee."; }
        catch (WalletWasabi.Exceptions.TransactionFeeOverpaymentException) { PaymentStatus.Text = "The fee exceeds the payment amount. Check the amount and fee rate."; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            PaymentStatus.Text = "Could not build this draft. Complete sync and check the amount and fee; very small outputs may be rejected.";
        }
        finally
        {
            _paymentCancellation = null;
            cancellation.Dispose();
            ReviewPaymentButton.IsEnabled = true;
        }
    }

    private async void SignPayment(object? sender, RoutedEventArgs args)
    {
        if (_paymentCancellation is not null || _broadcasting || _signedPayment is not null ||
            _paymentDraft is not { } draft || _sync is not { } sync) return;

        var passphrase = SigningPassphrase.Text ?? "";
        SigningPassphrase.Text = "";
        using var cancellation = new CancellationTokenSource();
        _paymentCancellation = cancellation;
        SignPaymentButton.IsEnabled = false;
        ReviewPaymentButton.IsEnabled = false;
        SigningResult.Text = "Signing on this device…";
        try
        {
            var signed = await sync.SignPaymentAsync(draft, passphrase, cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_paymentDraft, draft))
            {
                _signedPayment = signed;
                SigningResult.Text = $"Signed locally.\nTransaction ID: {_signedPayment.GetHash()}";
                BroadcastPanel.IsVisible = true;
            }
        }
        catch (System.Security.SecurityException)
        {
            if (!cancellation.IsCancellationRequested) SigningResult.Text = "Incorrect recovery passphrase.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!cancellation.IsCancellationRequested) SigningResult.Text = "Could not sign this draft. Check sync and build a new review.";
        }
        finally
        {
            _paymentCancellation = null;
            SignPaymentButton.IsEnabled = _signedPayment is null;
            ReviewPaymentButton.IsEnabled = true;
        }
    }

    private async void BroadcastPayment(object? sender, RoutedEventArgs args)
    {
        if (_broadcasting || BroadcastConsent.IsChecked != true || _paymentDraft is not { } draft ||
            _signedPayment is not { } signed || _sync is not { } sync ||
            _syncCancellation is not { IsCancellationRequested: false } cancellation) return;

        _broadcasting = true;
        BroadcastConsent.IsChecked = false;
        BroadcastPanel.IsVisible = false;
        ReviewPaymentButton.IsEnabled = false;
        var txId = signed.GetHash();
        BroadcastResult.Text = $"Submitting to Testnet4…\nTransaction ID: {txId}";
        try
        {
            await sync.BroadcastPaymentAsync(draft, signed, cancellation.Token);
            BroadcastResult.Text = $"Broadcast reported successful. Awaiting block confirmation.\nTransaction ID: {txId}";
        }
        catch (Exception)
        {
            BroadcastResult.Text = $"Broadcast was not verified. Check Testnet4 for this transaction before trying again.\nTransaction ID: {txId}";
            BroadcastPanel.IsVisible = ReferenceEquals(_signedPayment, signed) && !cancellation.IsCancellationRequested;
        }
        finally
        {
            _broadcasting = false;
            ReviewPaymentButton.IsEnabled = true;
        }
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
