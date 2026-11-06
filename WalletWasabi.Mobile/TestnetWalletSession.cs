using System;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;

namespace WalletWasabi.Mobile;

/// <summary>A testnet key manager kept in memory until the session ends.</summary>
public sealed class TestnetWalletSession
{
    private readonly KeyManager _keyManager;

    private TestnetWalletSession(KeyManager keyManager)
    {
        _keyManager = keyManager;
        var key = _keyManager.GetNextReceiveKey(LabelsArray.Empty, ScriptPubKeyType.TaprootBIP86);
        ReceivingAddress = key.GetAddress(Network.TestNet4).ToString();
        ReceivingKeyPath = key.FullKeyPath.ToString();
    }

    public string ReceivingAddress { get; }
    public string ReceivingKeyPath { get; }
    public string Fingerprint => _keyManager.MasterFingerprint?.ToString() ?? "";

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
