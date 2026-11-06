using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Mobile;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletStorageTests
{
    private const string LocalPassword = "local test unlock password";
    private const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public async Task StoredWalletReopensWithOriginalAddressAndExactPassword()
    {
        var storage = new MemoryStorage();
        var original = TestnetWalletSession.Recover(Words, "original passphrase");
        await new ProtectedWalletStore(storage).SaveNewAsync(original, LocalPassword);
        var saved = Encoding.UTF8.GetString(WalletEnvelope.Decrypt(storage.Data!, LocalPassword));
        Assert.DoesNotContain(Words, saved);
        Assert.DoesNotContain("original passphrase", saved);
        var reopened = await new ProtectedWalletStore(storage).UnlockAsync(LocalPassword);
        Assert.Equal(original.ReceivingAddress, reopened.ReceivingAddress);
        Assert.Null(KeyManager.FromJson(reopened.ExportEncryptedWallet()).FilePath);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => new ProtectedWalletStore(storage).UnlockAsync("wrong"));
    }

    [Fact]
    public async Task ExistingWalletCannotBeOverwritten()
    {
        var storage = new MemoryStorage();
        var store = new ProtectedWalletStore(storage);
        var session = TestnetWalletSession.Recover(Words, "");
        await store.SaveNewAsync(session, LocalPassword);
        var before = (byte[])storage.Data!.Clone();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveNewAsync(session, LocalPassword));
        Assert.Equal(before, storage.Data);
    }

    [Fact]
    public async Task CorruptDataFailsWithoutReplacingIt()
    {
        var storage = new MemoryStorage { Data = Encoding.UTF8.GetBytes("broken wallet") };
        await Assert.ThrowsAnyAsync<Exception>(() => new ProtectedWalletStore(storage).UnlockAsync(""));
        Assert.Equal("broken wallet", Encoding.UTF8.GetString(storage.Data));
    }

    [Fact]
    public void MainnetWalletCannotBeUnlockedInTestnetApp()
    {
        var manager = KeyManager.CreateNew(out _, "", Network.Main);
        Assert.Throws<FormatException>(() => TestnetWalletSession.Unlock(manager.ToJson(), ""));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(29)]
    [InlineData(45)]
    public void TamperingCannotProduceWalletData(int position)
    {
        var encrypted = WalletEnvelope.Encrypt(Encoding.UTF8.GetBytes("test wallet"), LocalPassword);
        encrypted[position] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => WalletEnvelope.Decrypt(encrypted, LocalPassword));
    }

    [Fact]
    public void EachSaveUsesFreshSaltAndNonce()
    {
        var plaintext = Encoding.UTF8.GetBytes("test wallet");
        Assert.NotEqual(WalletEnvelope.Encrypt(plaintext, LocalPassword), WalletEnvelope.Encrypt(plaintext, LocalPassword));
    }

    [Theory]
    [InlineData("abandon", "abandon", "about", true)]
    [InlineData("about", "abandon", "abandon", false)]
    [InlineData("", "abandon", "about", false)]
    public void BackupConfirmationChecksWordPositions(string first, string sixth, string last, bool expected) =>
        Assert.Equal(expected, WalletSetupView.VerifyBackup(Words, first, sixth, last));

    private sealed class MemoryStorage : IDeviceWalletStorage
    {
        public byte[]? Data { get; set; }
        public bool Exists => Data is not null;
        public Task SaveNewAsync(byte[] payload) { Data = (byte[])payload.Clone(); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync() => Task.FromResult((byte[])Data!.Clone());
    }
}
