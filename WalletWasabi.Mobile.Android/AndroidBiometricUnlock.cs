using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using CipherMode = Javax.Crypto.CipherMode;

namespace WalletWasabi.Mobile.Android;

/// <summary>Android 11+ strong biometrics bound to an auth-per-operation Keystore key.</summary>
[SupportedOSPlatform("android30.0")]
public sealed class AndroidBiometricUnlock(Activity activity, string directory) : IBiometricUnlock
{
    private const int Strong = 15;
    private const string Prefix = "wasabi.mobile.testnet4.biometric.";
    private string FilePath => Path.Combine(directory, "testnet4.biometric.protected");
    public bool IsEnabled => File.Exists(FilePath);
    public bool IsAvailable => OperatingSystem.IsAndroidVersionAtLeast(30) &&
        activity.GetSystemService(Context.BiometricService) is BiometricManager manager && manager.CanAuthenticate(Strong) == 0;

    private byte[] AssociatedData() => SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "testnet4.wallet.protected")));

    private byte[] ReadRecord()
    {
        if (new FileInfo(FilePath).Length is < 45 or > 8192) throw new IOException("Invalid biometric record size.");
        return File.ReadAllBytes(FilePath);
    }

    private static string Alias(byte[] record)
    {
        if (record.Length is < 45 or > 8192 || record[0] != 1) throw new IOException("Invalid biometric record.");
        return Prefix + new Guid(record.AsSpan(1, 16)).ToString("N");
    }

    public async Task EnableAsync(string password, CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("Strong biometrics unavailable.");
        if (Encoding.UTF8.GetByteCount(password) > 8000) throw new ArgumentException("Password too long for biometric storage.", nameof(password));
        var id = Guid.NewGuid();
        var alias = Prefix + id.ToString("N");
        var old = IsEnabled ? ReadRecord() : null;
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        var committed = false;
        try
        {
            using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            using var builder = new KeyGenParameterSpec.Builder(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            using var spec = builder.SetKeySize(256)!.SetBlockModes(KeyProperties.BlockModeGcm)!
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
                .SetUserAuthenticationRequired(true)!
                .SetUserAuthenticationParameters(0, (int)KeyPropertiesAuthType.BiometricStrong)!
                .SetInvalidatedByBiometricEnrollment(true)!.Build()!;
            generator.Init(spec);
            using var generated = generator.GenerateKey();
            using var key = store.GetKey(alias, null)!;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, key);
            await AuthenticateAsync(cipher, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            cipher.UpdateAAD(AssociatedData());
            var bytes = Encoding.UTF8.GetBytes(password);
            byte[] encrypted;
            try { encrypted = cipher.DoFinal(bytes)!; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            var nonce = cipher.GetIV()!;
            if (nonce.Length != 12) throw new IOException("Unexpected nonce size.");
            var record = new byte[29 + encrypted.Length];
            record[0] = 1;
            id.TryWriteBytes(record.AsSpan(1, 16));
            nonce.CopyTo(record, 17);
            encrypted.CopyTo(record, 29);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var temporary = FilePath + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(record); stream.Flush(true); }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, FilePath, true);
                committed = true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (old is not null)
            {
                try { store.DeleteEntry(Alias(old)); }
                catch (IOException) { /* A malformed old record cannot identify another key. */ }
            }
        }
        finally { if (!committed) store.DeleteEntry(alias); }
    }

    public async Task<string> ReadPasswordAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new InvalidOperationException("Strong biometrics unavailable.");
        var record = ReadRecord();
        var alias = Alias(record);
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        using var key = store.GetKey(alias, null) ?? throw new IOException("Biometric key unavailable. Use password unlock.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var parameters = new GCMParameterSpec(128, record[17..29]);
        cipher.Init(CipherMode.DecryptMode, key, parameters);
        await AuthenticateAsync(cipher, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        cipher.UpdateAAD(AssociatedData());
        var plaintext = cipher.DoFinal(record[29..])!;
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        var record = File.ReadAllBytes(FilePath);
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        try { store.DeleteEntry(Alias(record)); }
        catch (IOException) { /* Remove malformed optional record, leaving the wallet intact. */ }
        File.Delete(FilePath);
    }

    private async Task AuthenticateAsync(Cipher cipher, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new CancellationSignal();
        using var registration = token.Register(() => { signal.Cancel(); completion.TrySetCanceled(token); });
        using var callback = new AuthenticationCallback(cipher, completion);
        using var negative = new NegativeButton(completion);
        using var builder = new BiometricPrompt.Builder(activity);
        using var prompt = builder.SetTitle("Wasabi Wallet")!
            .SetSubtitle("Authenticate to use biometric wallet unlock")!
            .SetAllowedAuthenticators(Strong)!
            .SetNegativeButton("Use password", activity.MainExecutor!, negative)!.Build()!;
        using var crypto = new BiometricPrompt.CryptoObject(cipher);
        prompt.Authenticate(crypto, signal, activity.MainExecutor!, callback);
        await completion.Task;
    }

    private sealed class AuthenticationCallback(Cipher expected, TaskCompletionSource<bool> completion) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        {
            if (result?.CryptoObject?.Cipher is { } authenticated && authenticated.Equals(expected)) completion.TrySetResult(true);
            else completion.TrySetException(new InvalidOperationException("Missing authenticated crypto operation."));
        }
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            completion.TrySetException(new InvalidOperationException("System biometric authentication did not complete."));
    }

    private sealed class NegativeButton(TaskCompletionSource<bool> completion) : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => completion.TrySetCanceled();
    }
}
