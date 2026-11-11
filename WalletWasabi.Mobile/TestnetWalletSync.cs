using System;
using System.Collections.Concurrent;
using System.Net;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Client;
using WalletWasabi.Client.Configuration;
using WalletWasabi.Extensions;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Wallets;

namespace WalletWasabi.Mobile;

public record WalletSyncSnapshot(
    string Status, uint ScannedHeight, uint NetworkHeight, int PeerCount,
    long? BalanceSatoshis, string[] Transactions)
{
    public uint BlockHeaderHeight { get; init; }
    public uint FilterHeaderHeight { get; init; }
    public uint FilterHeight { get; init; }
    public int FilterPeerCount { get; init; }
}

public sealed class TestnetWalletSync
{
    private readonly SemaphoreSlim _walletGate = new(1, 1);
    private Wallet? _wallet;
    private Global? _global;
    private Func<bool>? _isReady;
    private CancellationToken _runCancellation;

    public async Task<TestnetPaymentReview> BuildPaymentReviewAsync(TestnetPaymentRequest request, CancellationToken token) =>
        (await BuildPaymentDraftAsync(request, token)).Review;

    internal async Task<TestnetPaymentDraft> BuildPaymentDraftAsync(TestnetPaymentRequest request, CancellationToken token)
    {
        await _walletGate.WaitAsync(token);
        try
        {
            if (_wallet is not { } wallet || _isReady?.Invoke() != true)
                throw new InvalidOperationException("Start sync and wait for a complete connected wallet scan before reviewing a payment.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _runCancellation);
            linked.Token.ThrowIfCancellationRequested();
            var draft = await Task.Run(() => TestnetPaymentBuilder.BuildDraft(wallet.KeyManager, wallet.Coins, wallet.TransactionStore, request), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (_isReady?.Invoke() != true) throw new InvalidOperationException("Sync changed. Complete synchronization and review again.");
            return draft;
        }
        finally { _walletGate.Release(); }
    }

    internal async Task<NBitcoin.Transaction> SignPaymentAsync(TestnetPaymentDraft draft, string recoveryPassphrase, CancellationToken token)
    {
        await _walletGate.WaitAsync(token);
        try
        {
            if (_wallet is not { } wallet || _isReady?.Invoke() != true)
                throw new InvalidOperationException("Complete synchronization before signing.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _runCancellation);
            var signed = await Task.Run(() => draft.Sign(wallet.KeyManager, wallet.Coins, recoveryPassphrase), linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (_isReady?.Invoke() != true)
                throw new InvalidOperationException("Sync changed. Build a new review before signing.");
            return signed;
        }
        finally { _walletGate.Release(); }
    }
    internal async Task BroadcastPaymentAsync(TestnetPaymentDraft draft, NBitcoin.Transaction signed, CancellationToken token)
    {
        await _walletGate.WaitAsync(token);
        try
        {
            if (_global is not { } global || global.Network != NBitcoin.Network.TestNet4 ||
                _wallet is not { } wallet || _isReady?.Invoke() != true)
                throw new InvalidOperationException("Complete Testnet4 synchronization before broadcasting.");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _runCancellation);
            draft.VerifyForBroadcast(signed, wallet.Coins);
            linked.Token.ThrowIfCancellationRequested();
            await global.TransactionBroadcaster.SendTransactionAsync(
                new WalletWasabi.Blockchain.Transactions.SmartTransaction(signed), linked.Token);
        }
        finally { _walletGate.Release(); }
    }

    public static Config CreateConfiguration(string dataDirectory) =>
        new(PersistentConfigManager.DefaultTestNetConfig with
        {
            Network = NBitcoin.Network.TestNet4,
            UseTor = "EnabledOnlyRunning",
            TerminateTorOnExit = false,
            DownloadNewVersion = false,
            JsonRpcServerEnabled = false,
            BitcoinRpcUri = "",
            CoordinatorUri = "",
            ExchangeRateProvider = "None",
            FeeRateEstimationProvider = "None",
            ExperimentalFeatures = []
        }, [
            "--UseTor=EnabledOnlyRunning", "--TorSocksPort=9050",
            $"--TorFolder={Path.Combine(dataDirectory, "ExternalTor")}"
        ]);

    public async Task RunAsync(TestnetWalletSession session, string dataDirectory,
        IProgress<WalletSyncSnapshot> progress, CancellationToken cancellationToken)
    {
        var config = CreateConfiguration(dataDirectory);
        // Guard effective configuration as desktop Config also accepts environment overrides.
        if (config.Network != NBitcoin.Network.TestNet4 ||
            config.UseTor != WalletWasabi.Models.TorMode.EnabledOnlyRunning ||
            config.JsonRpcServerEnabled || !string.IsNullOrEmpty(config.BitcoinRpcUri) ||
            !string.IsNullOrEmpty(config.CoordinatorUri))
        {
            throw new InvalidOperationException("The mobile sync configuration must use Testnet4 and external Tor.");
        }

        Global? global = null;
        var peers = new ConcurrentDictionary<EndPoint, (uint Height, bool SupportsFilters)>();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            progress.Report(new("Connecting to Tor on 127.0.0.1:9050…", 0, 0, 0, null, []));
            global = new Global(dataDirectory, config);
            using var added = global.EventBus.Subscribe<P2pNodeAdded>(e =>
                peers[e.EndPoint] = ((uint)Math.Max(0, e.Node.PeerVersion.StartHeight), e.Node.SupportsCompactFilters));
            using var removed = global.EventBus.Subscribe<P2pNodeRemoved>(e => peers.TryRemove(e.EndPoint, out _));
            var wallet = global.WalletManager.AddWallet(session.KeyManager);
            await global.InitializeAsync(false,
                new TerminateService(() => Task.CompletedTask, () => { }),
                cancellationToken, checkForUpdates: false).ConfigureAwait(false);

            var startingWallet = global.WalletManager.StartWalletAsync(wallet);
            _runCancellation = cancellationToken;
            _global = global;
            _wallet = wallet;
            _isReady = () => !cancellationToken.IsCancellationRequested && IsSynchronized(
                global.Status.IsTorRunning, peers.Count, (uint)session.KeyManager.GetBestHeight(),
                Math.Max(global.Status.BestNetworkHeight, peers.Values.Select(peer => peer.Height).DefaultIfEmpty().Max()),
                global.FilterHeaders.HashesLeft, startingWallet.IsCompletedSuccessfully);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (startingWallet.IsFaulted)
                {
                    await startingWallet.ConfigureAwait(false);
                }

                var scanned = (uint)session.KeyManager.GetBestHeight();
                var connectedPeers = peers.Values;
                var tip = Math.Max(global.Status.BestNetworkHeight, connectedPeers.Select(peer => peer.Height).DefaultIfEmpty().Max());
                var connected = connectedPeers.Count;
                var filterPeers = connectedPeers.Count(peer => peer.SupportsFilters);
                var blockHeaderHeight = global.GetBlockHeadersTipHeight();
                var filterHeaderHeight = (uint)global.FilterHeaders.TipHeight;
                var filterHeight = (uint)(global.FilterStore.GetTip()?.Header.Height ?? 0);
                var synced = IsSynchronized(global.Status.IsTorRunning, connected, scanned, tip,
                    global.FilterHeaders.HashesLeft, startingWallet.IsCompletedSuccessfully);
                var status = !global.Status.IsTorRunning ? "Tor disconnected. Waiting for reconnection."
                    : connected == 0 ? "Tor connected. Discovering Testnet4 peers…"
                    : synced ? "Synchronized through Tor"
                    : filterPeers == 0 && filterHeight < blockHeaderHeight ? "Waiting for a Testnet4 peer that serves compact filters…"
                    : "Downloading headers and filters, then scanning wallet…";
                var transactions = synced
                    ? wallet.GetTransactions().Select(tx => $"{tx.Height}  {tx.GetHash()}").ToArray()
                    : [];
                progress.Report(new(status, scanned, tip, connected,
                    synced ? wallet.Coins.TotalAmount().Satoshi : null, transactions)
                {
                    BlockHeaderHeight = blockHeaderHeight,
                    FilterHeaderHeight = filterHeaderHeight,
                    FilterHeight = filterHeight,
                    FilterPeerCount = filterPeers
                });
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // Shutdown must wait for any draft build even after sync cancellation.
            await _walletGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _wallet = null;
                _global = null;
                _isReady = null;
                if (global is not null) await global.DisposeAsync().ConfigureAwait(false);
            }
            finally { _walletGate.Release(); }
        }
    }

    public static bool IsSynchronized(bool torRunning, int peers, uint scanned, uint tip,
        int filtersLeft, bool walletLoaded) =>
        torRunning && peers > 0 && tip > 0 && scanned >= tip && filtersLeft == 0 && walletLoaded;
}
