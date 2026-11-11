using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Mobile;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletBiometricTests
{
    private const string Password = "localtestpassword";
    private const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Fact]
    public async Task WrongPasswordNeverEnablesBiometrics()
    {
        var (store, bio) = await CreateAsync();
        await Assert.ThrowsAnyAsync<CryptographicException>(() => new BiometricWalletAccess(store, bio).EnableAndUnlockAsync("wrong", default));
        Assert.Equal(0, bio.EnableCalls);
        Assert.False(bio.IsEnabled);
    }

    [Fact]
    public async Task VerifiedEnrollmentAndBiometricUnlockPreserveAddress()
    {
        var (store, bio) = await CreateAsync();
        var access = new BiometricWalletAccess(store, bio);
        var enrolled = await access.EnableAndUnlockAsync(Password, default);
        var unlocked = await access.UnlockAsync(default);
        Assert.Equal(enrolled.ReceivingAddress, unlocked.ReceivingAddress);
        bio.Disable();
        Assert.Equal(enrolled.ReceivingAddress, (await store.UnlockAsync(Password)).ReceivingAddress);
    }

    [Fact]
    public async Task LateBiometricResultCannotUnlockAfterCancellation()
    {
        var (store, bio) = await CreateAsync();
        using var cancellation = new CancellationTokenSource();
        bio.Read = () => { cancellation.Cancel(); return Task.FromResult(Password); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BiometricWalletAccess(store, bio).UnlockAsync(cancellation.Token));
        Assert.NotNull(await store.UnlockAsync(Password));
    }

    [Fact]
    public async Task WrongBiometricSecretCannotBypassWalletEncryption()
    {
        var (store, bio) = await CreateAsync();
        bio.Read = () => Task.FromResult("wrong");
        await Assert.ThrowsAnyAsync<CryptographicException>(() => new BiometricWalletAccess(store, bio).UnlockAsync(default));
        Assert.NotNull(await store.UnlockAsync(Password));
    }

    [Fact]
    public async Task CancelledEnrollmentLeavesPasswordUnlockAvailable()
    {
        var (store, bio) = await CreateAsync();
        bio.CancelEnrollment = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BiometricWalletAccess(store, bio).EnableAndUnlockAsync(Password, default));
        Assert.False(bio.IsEnabled);
        Assert.NotNull(await store.UnlockAsync(Password));
    }

    private static async Task<(ProtectedWalletStore, FakeBiometrics)> CreateAsync()
    {
        var store = new ProtectedWalletStore(new MemoryStorage());
        await store.SaveNewAsync(TestnetWalletSession.Recover(Words, ""), Password);
        return (store, new FakeBiometrics());
    }

    private sealed class MemoryStorage : IDeviceWalletStorage
    {
        private byte[]? _data;
        public bool Exists => _data is not null;
        public Task SaveNewAsync(byte[] payload) { _data = (byte[])payload.Clone(); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync() => Task.FromResult((byte[])_data!.Clone());
    }

    private sealed class FakeBiometrics : IBiometricUnlock
    {
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        public int EnableCalls { get; private set; }
        public bool CancelEnrollment { get; set; }
        public Func<Task<string>> Read { get; set; } = () => Task.FromResult(Password);
        public Task EnableAsync(string password, CancellationToken token)
        {
            EnableCalls++;
            if (CancelEnrollment) throw new OperationCanceledException();
            IsEnabled = true;
            return Task.CompletedTask;
        }
        public Task<string> ReadPasswordAsync(CancellationToken token) => Read();
        public void Disable() => IsEnabled = false;
    }
}
