using System;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Mobile;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletSessionTests
{
    // Public BIP39 test vector, used only for derivation checks.
    private const string Words = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    [Theory]
    [InlineData("")]
    [InlineData("test passphrase")]
    [InlineData("P@ssw0rdé")]
    public void RecoveryMatchesDesktop(string password)
    {
        var mobile = TestnetWalletSession.Recover(Words, password);
        var desktop = KeyManager.Recover(new Mnemonic(Words), password, Network.TestNet4,
            KeyManager.GetAccountKeyPath(Network.TestNet4, ScriptPubKeyType.Segwit),
            KeyManager.GetAccountKeyPath(Network.TestNet4, ScriptPubKeyType.TaprootBIP86));
        var key = desktop.GetNextReceiveKey(LabelsArray.Empty, ScriptPubKeyType.TaprootBIP86);

        Assert.Equal(key.GetAddress(Network.TestNet4).ToString(), mobile.ReceivingAddress);
        Assert.Equal(key.FullKeyPath.ToString(), mobile.ReceivingKeyPath);
        Assert.Equal(desktop.MasterFingerprint?.ToString(), mobile.Fingerprint);
        Assert.StartsWith("tb1p", mobile.ReceivingAddress);
    }

    [Fact]
    public void CreatedWalletRecoversTheSameAddress()
    {
        var created = TestnetWalletSession.Create("test passphrase");
        var recovered = TestnetWalletSession.Recover(created.RecoveryWords, "test passphrase");
        Assert.Equal(created.Session.ReceivingAddress, recovered.ReceivingAddress);
        Assert.Equal(created.Session.Fingerprint, recovered.Fingerprint);
        Assert.Equal(12, created.RecoveryWords.Split(' ').Length);
    }

    [Fact]
    public void ExactPasswordDeterminesWallet()
    {
        Assert.NotEqual(
            TestnetWalletSession.Recover(Words, "password").ReceivingAddress,
            TestnetWalletSession.Recover(Words, "password ").ReceivingAddress);
    }

    [Fact]
    public void RecoveryMatchesExplicitBip86Derivation()
    {
        var mobile = TestnetWalletSession.Recover(Words, "");
        var key = new Mnemonic(Words).DeriveExtKey("")
            .Derive(new KeyPath("86'/1'/0'/0/0"));

        Assert.Equal(key.PrivateKey.PubKey.GetAddress(
            ScriptPubKeyType.TaprootBIP86, Network.TestNet4).ToString(), mobile.ReceivingAddress);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("  ")]
    public void RecoveryAcceptsWhitespaceBetweenWords(string separator)
    {
        var expected = TestnetWalletSession.Recover(Words, "").ReceivingAddress;
        var recovered = TestnetWalletSession.Recover(
            "  " + Words.Replace(" ", separator, StringComparison.Ordinal) + "  ", "");
        Assert.Equal(expected, recovered.ReceivingAddress);
    }

    [Fact]
    public void InvalidChecksumIsRejected()
    {
        var invalidWords = Words.Replace("about", "abandon", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => TestnetWalletSession.Recover(invalidWords, ""));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WatchOnlyImportMatchesSignerAndRestoresWithoutPrivateKeys(bool includeTaproot)
    {
        var network = Network.TestNet4;
        var master = new Mnemonic(Words).DeriveExtKey("");
        var segwit = master.Derive(new KeyPath("84'/1'/0'")).Neuter();
        var taproot = master.Derive(new KeyPath("86'/1'/0'")).Neuter();
        var segwitAddress = segwit.Derive(0).Derive(0).PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
        var taprootAddress = includeTaproot
            ? taproot.Derive(0).Derive(0).PubKey.GetAddress(ScriptPubKeyType.TaprootBIP86, network).ToString()
            : null;
        var address = taprootAddress ?? segwitAddress;
        var export = new System.Collections.Generic.Dictionary<string, object>
        {
            ["version"] = 1,
            ["network"] = "testnet4",
            ["masterFingerprint"] = master.PrivateKey.PubKey.GetHDFingerPrint().ToString(),
            ["segwitAccountXpub"] = segwit.GetWif(network).ToString()
        };
        if (includeTaproot)
            export["taprootAccountXpub"] = taproot.GetWif(network).ToString();
        var json = System.Text.Json.JsonSerializer.Serialize(export);

        var session = TestnetWalletSession.ImportWatchOnly(json, segwitAddress, taprootAddress);
        Assert.True(session.IsWatchOnly);
        Assert.Equal(address, session.ReceivingAddress);
        Assert.Null(session.KeyManager.EncryptedSecret);
        var restored = TestnetWalletSession.RestoreProtected(session.ExportEncryptedWallet());
        Assert.True(restored.IsWatchOnly);
        Assert.Equal(address, restored.ReceivingAddress);

        var wrongAddress = segwit.Derive(0).Derive(1).PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
        Assert.Throws<FormatException>(() => TestnetWalletSession.ImportWatchOnly(json, wrongAddress, taprootAddress));
        if (includeTaproot)
        {
            Assert.Throws<FormatException>(() => TestnetWalletSession.ImportWatchOnly(json, segwitAddress));
            Assert.Throws<FormatException>(() => TestnetWalletSession.ImportWatchOnly(json, segwitAddress, wrongAddress));
        }
    }
}
