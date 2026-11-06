using System;
using System.Collections.Concurrent;
using System.Net;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Client;
using WalletWasabi.Client.Configuration;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Wallets;

namespace WalletWasabi.Mobile;

public record WalletSyncSnapshot(
    string Status, uint ScannedHeight, uint NetworkHeight, int PeerCount,
    long? BalanceSatoshis, string[] Transactions);

public sealed class TestnetWalletSync
{
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
        var peers = new ConcurrentDictionary<EndPoint, uint>();
        try
        {
            Directory.CreateDirectory(dataDirectory);
            progress.Report(new("Connecting to Tor on 127.0.0.1:9050…", 0, 0, 0, null, []));
            global = new Global(dataDirectory, config);
            using var added = global.EventBus.Subscribe<P2pNodeAdded>(e =>
                peers[e.EndPoint] = (uint)Math.Max(0, e.Node.PeerVersion.StartHeight));
            using var removed = global.EventBus.Subscribe<P2pNodeRemoved>(e => peers.TryRemove(e.EndPoint, out _));
            var wallet = global.WalletManager.AddWallet(session.KeyManager);
            await global.InitializeAsync(false,
                new TerminateService(() => Task.CompletedTask, () => { }),
                cancellationToken, checkForUpdates: false).ConfigureAwait(false);

            var startingWallet = global.WalletManager.StartWalletAsync(wallet);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (startingWallet.IsFaulted)
                {
                    await startingWallet.ConfigureAwait(false);
                }

                var scanned = (uint)session.KeyManager.GetBestHeight();
                var tip = Math.Max(global.Status.BestNetworkHeight, peers.Values.DefaultIfEmpty().Max());
                var connected = peers.Count;
                var synced = IsSynchronized(global.Status.IsTorRunning, connected, scanned, tip,
                    global.FilterHeaders.HashesLeft, startingWallet.IsCompletedSuccessfully);
                var status = !global.Status.IsTorRunning ? "Tor disconnected. Waiting for reconnection."
                    : connected == 0 ? "Tor connected. Discovering Testnet4 peers…"
                    : synced ? "Synchronized through Tor"
                    : "Downloading headers and filters, then scanning wallet…";
                var transactions = synced
                    ? wallet.GetTransactions().Select(tx => $"{tx.Height}  {tx.GetHash()}").ToArray()
                    : [];
                progress.Report(new(status, scanned, tip, connected,
                    synced ? wallet.Coins.TotalAmount().Satoshi : null, transactions));
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (global is not null)
            {
                await global.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static bool IsSynchronized(bool torRunning, int peers, uint scanned, uint tip,
        int filtersLeft, bool walletLoaded) =>
        torRunning && peers > 0 && tip > 0 && scanned >= tip && filtersLeft == 0 && walletLoaded;
}
