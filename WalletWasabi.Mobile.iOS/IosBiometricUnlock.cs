using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation;
using LocalAuthentication;
using Security;

namespace WalletWasabi.Mobile.iOS;

internal sealed class IosBiometricUnlock(IosWalletStorage storage) : IBiometricUnlock
{
    private const string EnabledKey = "wallet.biometric.enabled";
    private const LAPolicy BiometricPolicy = LAPolicy.DeviceOwnerAuthenticationWithBiometrics;

    public bool IsAvailable
    {
        get
        {
            using var context = new LAContext();
            return context.CanEvaluatePolicy(BiometricPolicy, out _);
        }
    }

    public bool IsEnabled => NSUserDefaults.StandardUserDefaults.BoolForKey(EnabledKey);

    public async Task EnableAsync(string password, CancellationToken cancellationToken)
    {
        var walletHash = SHA256.HashData(await storage.ReadAsync());
        using var context = new LAContext { LocalizedFallbackTitle = "" };
        using var cancellation = cancellationToken.Register(context.Invalidate);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EvaluatePolicy(BiometricPolicy, "Enable biometric unlock for your Wasabi wallet.",
            (success, _) => result.TrySetResult(success));
        if (!await result.Task || cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        var credential = new byte[walletHash.Length + Encoding.UTF8.GetByteCount(password)];
        walletHash.CopyTo(credential, 0);
        Encoding.UTF8.GetBytes(password.AsSpan(), credential.AsSpan(walletHash.Length));
        try
        {
            using var item = Query();
            using var access = new SecAccessControl(SecAccessible.WhenPasscodeSetThisDeviceOnly,
                SecAccessControlCreateFlags.BiometryCurrentSet);
            using var data = NSData.FromArray(credential);
            item.AccessControl = access;
            item.Synchronizable = false;
            item.ValueData = data;
            cancellationToken.ThrowIfCancellationRequested();
            if (SecKeyChain.Add(item) != SecStatusCode.Success)
                throw new IOException("Could not save biometric unlock in Keychain.");
            NSUserDefaults.StandardUserDefaults.SetBool(true, EnabledKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credential);
        }
    }

    public async Task<string> ReadPasswordAsync(CancellationToken cancellationToken)
    {
        var walletHash = SHA256.HashData(await storage.ReadAsync());
        return await Task.Run(() =>
        {
            using var context = new LAContext
            {
                LocalizedFallbackTitle = "",
                LocalizedReason = "Unlock your Wasabi wallet."
            };
            using var cancellation = cancellationToken.Register(context.Invalidate);
            using var query = Query();
            query.AuthenticationContext = context;
            cancellationToken.ThrowIfCancellationRequested();
            using var data = SecKeyChain.QueryAsData(query, false, out var status);
            cancellationToken.ThrowIfCancellationRequested();
            if (status == SecStatusCode.UserCanceled)
                throw new OperationCanceledException(cancellationToken);
            if (status != SecStatusCode.Success || data is null)
                throw new IOException("Biometric unlock is unavailable.");

            var credential = data.ToArray();
            try
            {
                if (credential.Length <= walletHash.Length ||
                    !CryptographicOperations.FixedTimeEquals(walletHash, credential.AsSpan(0, walletHash.Length)))
                    throw new IOException("The biometric credential belongs to a different wallet.");
                return Encoding.UTF8.GetString(credential.AsSpan(walletHash.Length));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credential);
            }
        }, cancellationToken);
    }

    public void Disable()
    {
        using var query = Query();
        var status = SecKeyChain.Remove(query);
        if (status != SecStatusCode.Success && status != SecStatusCode.ItemNotFound)
            throw new IOException("Could not remove biometric unlock from Keychain.");
        NSUserDefaults.StandardUserDefaults.RemoveObject(EnabledKey);
    }

    private static SecRecord Query() => new(SecKind.GenericPassword)
    {
        Service = "io.github.argusretriever.wasabiwalletmobile.testnet4",
        Account = "wallet.biometric.v1"
    };
}
