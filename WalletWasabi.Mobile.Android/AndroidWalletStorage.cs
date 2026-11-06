using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace WalletWasabi.Mobile.Android;

public sealed class AndroidWalletStorage(string directory) : IDeviceWalletStorage
{
    private const string Alias = "wasabi.mobile.testnet4.wallet.v1";
    private static readonly byte[] Context = Encoding.UTF8.GetBytes(Alias);
    private string WalletPath => Path.Combine(directory, "testnet4.wallet.protected");
    public bool Exists => File.Exists(WalletPath);

    public Task SaveNewAsync(byte[] payload) => Task.Run(() =>
    {
        if (Exists) throw new IOException("A saved wallet already exists.");
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (!store.ContainsAlias(Alias))
        {
            using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            using var builder = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            using var spec = builder.SetKeySize(256)!
                .SetBlockModes(KeyProperties.BlockModeGcm)!
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
                .SetRandomizedEncryptionRequired(true)!.Build()!;
            generator.Init(spec);
            using var generated = generator.GenerateKey();
        }
        using var key = store.GetKey(Alias, null) ?? throw new IOException("Device key unavailable.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, key);
        cipher.UpdateAAD(Context);
        var encrypted = cipher.DoFinal(payload)!;
        var iv = cipher.GetIV()!;
        if (iv.Length != 12) throw new IOException("Unexpected nonce size.");
        Directory.CreateDirectory(directory);
        var temporary = WalletPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(1);
                stream.Write(iv);
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, WalletPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });

    public Task<byte[]> ReadAsync() => Task.Run(() =>
    {
        var info = new FileInfo(WalletPath);
        if (info.Length is < 29 or > 2_000_000) throw new IOException("Invalid protected wallet size.");
        var envelope = File.ReadAllBytes(WalletPath);
        if (envelope[0] != 1) throw new IOException("Unsupported protected wallet version.");
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        using var key = store.GetKey(Alias, null) ?? throw new IOException("Device key unavailable.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var parameters = new GCMParameterSpec(128, envelope[1..13]);
        cipher.Init(CipherMode.DecryptMode, key, parameters);
        cipher.UpdateAAD(Context);
        return cipher.DoFinal(envelope[13..])!;
    });
}
