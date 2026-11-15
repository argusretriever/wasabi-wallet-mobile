using System;
using System.Collections.Generic;
using System.Text.Json;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;

namespace WalletWasabi.Mobile;

/// <summary>An open testnet wallet. The key manager never writes a plaintext wallet file.</summary>
public sealed class TestnetWalletSession
{
    private readonly KeyManager _keyManager;

    private TestnetWalletSession(KeyManager keyManager)
    {
        _keyManager = keyManager;
        var receiveType = keyManager.TaprootExtPubKey is null ? ScriptPubKeyType.Segwit : ScriptPubKeyType.TaprootBIP86;
        var key = _keyManager.GetNextReceiveKey(LabelsArray.Empty, receiveType);
        ReceivingAddress = key.GetAddress(Network.TestNet4).ToString();
        ReceivingKeyPath = key.FullKeyPath.ToString();
    }

    public string ReceivingAddress { get; }
    public string ReceivingKeyPath { get; }
    public string Fingerprint => _keyManager.MasterFingerprint?.ToString() ?? "";
    public bool IsWatchOnly => _keyManager.IsWatchOnly;
    internal KeyManager KeyManager => _keyManager;

    public string ExportEncryptedWallet() => _keyManager.ToJson();

    public static TestnetWalletSession Unlock(string json, string password)
    {
        var manager = KeyManager.FromJson(json);
        if (manager.GetNetwork() != Network.TestNet4 || manager.IsWatchOnly)
            throw new FormatException("Only Testnet4 software wallets are supported.");
        manager.GetMasterExtKey(password);
        return new TestnetWalletSession(manager);
    }

    internal static TestnetWalletSession RestoreProtected(string json)
    {
        var manager = KeyManager.FromJson(json);
        if (manager.GetNetwork() != Network.TestNet4 || (manager.IsWatchOnly && !manager.IsHardwareWallet))
            throw new FormatException("Only Testnet4 software and hardware watch-only wallets are supported.");
        return new TestnetWalletSession(manager);
    }

    public static TestnetWalletSession ImportWatchOnly(string json, string segwitAddress, string? taprootAddress = null)
    {
        if (json.Length > 4096)
            throw new FormatException("The watch-only export is too large.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Expected a watch-only JSON object.");

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("version" or "network" or "masterFingerprint" or
                "segwitAccountXpub" or "taprootAccountXpub") || !fields.Add(property.Name))
                throw new FormatException("The watch-only export contains an unknown or duplicate field.");
        }

        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number != 1 || ReadString(root, "network") != "testnet4")
            throw new FormatException("Expected a version 1 Testnet4 watch-only export.");

        var fingerprint = ReadString(root, "masterFingerprint");
        if (fingerprint.Length != 8)
            throw new FormatException("The master fingerprint must contain eight hexadecimal digits.");
        var segwit = ParseAccountKey(ReadString(root, "segwitAccountXpub"));
        var taproot = fields.Contains("taprootAccountXpub")
            ? ParseAccountKey(ReadString(root, "taprootAccountXpub")) : null;
        if (segwit.Equals(taproot))
            throw new FormatException("SegWit and Taproot require different account keys.");
        VerifySignerAddress(segwit, ScriptPubKeyType.Segwit, segwitAddress, "SegWit");
        if (taproot is not null)
            VerifySignerAddress(taproot, ScriptPubKeyType.TaprootBIP86, taprootAddress, "Taproot");
        else if (!string.IsNullOrWhiteSpace(taprootAddress))
            throw new FormatException("A Taproot address requires a Taproot account key.");

        var manager = KeyManager.CreateNewHardwareWalletWatchOnly(
            new HDFingerprint(Convert.FromHexString(fingerprint)), segwit, taproot, null, null, Network.TestNet4);
        return new TestnetWalletSession(manager);
    }

    private static void VerifySignerAddress(ExtPubKey account, ScriptPubKeyType type, string? address, string label)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new FormatException($"Enter the first {label} receiving address from the signer.");
        var supplied = BitcoinAddress.Create(address.Trim(), Network.TestNet4);
        var derived = account.Derive(0).Derive(0).PubKey.GetAddress(type, Network.TestNet4);
        if (supplied.ScriptPubKey != derived.ScriptPubKey)
            throw new FormatException($"The first {label} receiving address does not match the signer.");
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"Expected a string for {name}.");

    private static ExtPubKey ParseAccountKey(string encoded)
    {
        if (!encoded.StartsWith("tpub", StringComparison.Ordinal))
            throw new FormatException("Expected a Testnet extended public key.");
        var key = ExtPubKey.Parse(encoded, Network.TestNet4);
        if (key.Depth != 3 || key.Child != 0x80000000u)
            throw new FormatException("Expected a depth-three account-zero public key.");
        return key;
    }

    public static (TestnetWalletSession Session, string RecoveryWords) Create(string password)
    {
        var manager = KeyManager.CreateNew(out var mnemonic, password, Network.TestNet4);
        return (new TestnetWalletSession(manager), mnemonic.ToString());
    }

    public static TestnetWalletSession Recover(string words, string password)
    {
        var mnemonic = new Mnemonic(words, Wordlist.English);
        if (!mnemonic.IsValidChecksum)
        {
            throw new FormatException("Invalid recovery phrase checksum.");
        }

        var manager = KeyManager.Recover(
            mnemonic, password, Network.TestNet4,
            KeyManager.GetAccountKeyPath(Network.TestNet4, ScriptPubKeyType.Segwit),
            KeyManager.GetAccountKeyPath(Network.TestNet4, ScriptPubKeyType.TaprootBIP86));
        return new TestnetWalletSession(manager);
    }
}
