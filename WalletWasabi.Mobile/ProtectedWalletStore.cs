using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Mobile;

/// <summary>Platform storage must protect the complete payload and fail closed.</summary>
public interface IDeviceWalletStorage
{
    bool Exists { get; }
    Task SaveNewAsync(byte[] payload);
    Task<byte[]> ReadAsync();
}

public sealed class ProtectedWalletStore(IDeviceWalletStorage storage)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool Exists => storage.Exists;

    public async Task SaveNewAsync(TestnetWalletSession session, string unlockPassword)
    {
        if (unlockPassword.Length < 12 || string.IsNullOrWhiteSpace(unlockPassword))
            throw new ArgumentException("Use at least 12 characters for local unlock.", nameof(unlockPassword));
        await _gate.WaitAsync();
        try
        {
            if (storage.Exists) throw new InvalidOperationException("A saved wallet already exists.");
            var bytes = Encoding.UTF8.GetBytes(session.ExportEncryptedWallet());
            try
            {
                var encrypted = await Task.Run(() => WalletEnvelope.Encrypt(bytes, unlockPassword));
                await storage.SaveNewAsync(encrypted);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _gate.Release(); }
    }

    public async Task<TestnetWalletSession> UnlockAsync(string password)
    {
        await _gate.WaitAsync();
        try
        {
            var bytes = await storage.ReadAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var plaintext = WalletEnvelope.Decrypt(bytes, password);
                    try { return TestnetWalletSession.RestoreProtected(Encoding.UTF8.GetString(plaintext)); }
                    finally { CryptographicOperations.ZeroMemory(plaintext); }
                });
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _gate.Release(); }
    }
}

public static class WalletEnvelope
{
    private const int Iterations = 600_000;
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("WasabiMobile/Testnet4/Wallet/v1");

    public static byte[] Encrypt(byte[] plaintext, string password)
    {
        var result = new byte[45 + plaintext.Length];
        result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 16));
        RandomNumberGenerator.Fill(result.AsSpan(17, 12));
        var key = Rfc2898DeriveBytes.Pbkdf2(password, result.AsSpan(1, 16), Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(17, 12), plaintext, result.AsSpan(45), result.AsSpan(29, 16), Context);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] Decrypt(byte[] envelope, string password)
    {
        if (envelope.Length is < 45 or > 2_000_000 || envelope[0] != 1)
            throw new CryptographicException("Invalid protected wallet envelope.");
        var plaintext = new byte[envelope.Length - 45];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, envelope.AsSpan(1, 16), Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.AsSpan(17, 12), envelope.AsSpan(45), envelope.AsSpan(29, 16), plaintext, Context);
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
