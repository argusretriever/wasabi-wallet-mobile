using System.IO;
using NBitcoin;
using WalletWasabi.Mobile;
using WalletWasabi.Models;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletSyncTests
{
    [Fact]
    public void ConfigurationUsesExternalTorAndTestnet()
    {
        var config = TestnetWalletSync.CreateConfiguration(Path.GetTempPath());
        Assert.Equal(Network.TestNet4, config.Network);
        Assert.Equal(TorMode.EnabledOnlyRunning, config.UseTor);
        Assert.Equal(9050, config.TorSocksPort);
        Assert.False(config.JsonRpcServerEnabled);
        Assert.False(config.DownloadNewVersion);
        Assert.Empty(config.BitcoinRpcUri);
        Assert.Empty(config.CoordinatorUri);
    }

    [Theory]
    [InlineData(false, 3, 100u, 100u, 0, true)]
    [InlineData(true, 0, 100u, 100u, 0, true)]
    [InlineData(true, 3, 0u, 0u, 0, true)]
    [InlineData(true, 3, 50u, 100u, 0, true)]
    [InlineData(true, 3, 100u, 100u, 1, true)]
    [InlineData(true, 3, 100u, 100u, 0, false)]
    public void IncompleteOrDisconnectedScanCannotReportBalance(
        bool tor, int peers, uint scanned, uint tip, int filtersLeft, bool loaded)
    {
        Assert.False(TestnetWalletSync.IsSynchronized(tor, peers, scanned, tip, filtersLeft, loaded));
    }

    [Fact]
    public void CompletedConnectedScanCanReportBalance()
    {
        Assert.True(TestnetWalletSync.IsSynchronized(true, 3, 100, 100, 0, true));
    }
}
