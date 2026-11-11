using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Mobile;

public sealed class BiometricWalletAccess(ProtectedWalletStore store, IBiometricUnlock biometrics)
{
    public async Task<TestnetWalletSession> EnableAndUnlockAsync(string password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var session = await store.UnlockAsync(password);
        token.ThrowIfCancellationRequested();
        await biometrics.EnableAsync(password, token);
        token.ThrowIfCancellationRequested();
        return session;
    }

    public async Task<TestnetWalletSession> UnlockAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var password = await biometrics.ReadPasswordAsync(token);
        token.ThrowIfCancellationRequested();
        var session = await store.UnlockAsync(password);
        token.ThrowIfCancellationRequested();
        return session;
    }
}
