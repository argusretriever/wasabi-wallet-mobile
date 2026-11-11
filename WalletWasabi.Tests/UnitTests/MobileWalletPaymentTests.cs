using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Exceptions;
using WalletWasabi.Mobile;
using WalletWasabi.Tests.Helpers;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletPaymentTests
{
    private static string Address(Network network)
    {
        using var key = new Key();
        return key.PubKey.GetAddress(ScriptPubKeyType.Segwit, network).ToString();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.1")]
    [InlineData("1e-5")]
    [InlineData("0,00001")]
    [InlineData("0.000000001")]
    [InlineData("21000001")]
    [InlineData("NaN")]
    public void RejectsInvalidAmounts(string amount) =>
        Assert.Throws<FormatException>(() => TestnetPaymentRequest.Parse(Address(Network.TestNet4), amount, "1"));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1001")]
    [InlineData("1.0001")]
    [InlineData("1,5")]
    public void RejectsInvalidFees(string fee) =>
        Assert.Throws<FormatException>(() => TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00001", fee));

    [Fact]
    public void RejectsMainnetAndPaymentUris()
    {
        Assert.Throws<FormatException>(() => TestnetPaymentRequest.Parse(Address(Network.Main), "0.00001", "1"));
        Assert.Throws<FormatException>(() => TestnetPaymentRequest.Parse("bitcoin:" + Address(Network.TestNet4), "0.00001", "1"));
    }

    [Fact]
    public void AmountParsingIsIndependentOfDeviceCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = culture;
            var request = TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00001", "1.25");
            Assert.Equal(1000, request.Amount.Satoshi);
            Assert.Equal(1.25m, request.FeeRate.SatoshiPerByte);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void BuildsBalancedUnsignedReviewWithOwnedChangeWithoutPassword()
    {
        var (manager, coins, transactions) = FundedWallet();
        var request = TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00003", "1");
        var draft = TestnetPaymentBuilder.BuildDraft(manager, coins, transactions, request);
        var review = draft.Review;
        var psbt = draft.OpenUnsignedPsbt();
        var tx = psbt.GetGlobalTransaction();
        Assert.False(psbt.IsAllFinalized());
        Assert.NotSame(psbt, draft.OpenUnsignedPsbt());
        Assert.Equal(psbt.ToBase64(), draft.OpenUnsignedPsbt().ToBase64());
        Assert.Equal(review.InputCount, tx.Inputs.Count);
        Assert.Contains(tx.Outputs, output => output.ScriptPubKey == request.Destination.ScriptPubKey && output.Value == request.Amount);
        Assert.Equal(review.FeeSatoshis, review.InputSatoshis - tx.TotalOut.Satoshi);
        Assert.Equal(request.Destination.ToString(), review.Recipient);
        Assert.Equal(3000, review.AmountSatoshis);
        Assert.True(review.FeeSatoshis > 0);
        Assert.Equal(10_000, review.InputSatoshis);
        Assert.Equal(review.InputSatoshis, review.TotalDebitSatoshis + review.ChangeSatoshis);
        Assert.Single(review.Change);
        Assert.True(manager.TryGetKeyForScriptPubKey(BitcoinAddress.Create(review.Change[0].Address, Network.TestNet4).ScriptPubKey, out var changeKey));
        Assert.True(changeKey.IsInternal);
        Assert.Null(coins.Single().SpenderTransaction);
    }

    [Theory]
    [InlineData(ScriptPubKeyType.Segwit)]
    [InlineData(ScriptPubKeyType.TaprootBIP86)]
    public void SignsExactDraftAndRejectsUnavailableInputs(ScriptPubKeyType inputType)
    {
        var (manager, coins, transactions) = FundedWallet(inputType: inputType);
        var request = TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00003", "1");
        var draft = TestnetPaymentBuilder.BuildDraft(manager, coins, transactions, request);
        var unsigned = draft.OpenUnsignedPsbt().GetGlobalTransaction();

        Assert.Throws<System.Security.SecurityException>(() => draft.Sign(manager, coins, "wrong passphrase"));
        var signed = draft.Sign(manager, coins, "recovery passphrase");
        Assert.Equal(unsigned.GetHash(), signed.GetHash());
        Assert.NotEqual(unsigned.GetWitHash(), signed.GetWitHash());
        draft.VerifyForBroadcast(signed, coins);
        Assert.Throws<InvalidOperationException>(() => draft.VerifyForBroadcast(unsigned, coins));
        signed.Outputs[0].Value -= Money.Satoshis(1);
        Assert.Throws<InvalidOperationException>(() => draft.VerifyForBroadcast(signed, coins));
        Assert.False(draft.OpenUnsignedPsbt().IsAllFinalized());
        Assert.Null(coins.Single().SpenderTransaction);

        coins.Single().CoinJoinInProgress = true;
        Assert.Throws<InvalidOperationException>(() => draft.Sign(manager, coins, "recovery passphrase"));
    }

    [Fact]
    public void RejectsInsufficientOrUnconfirmedFunds()
    {
        var (manager, coins, transactions) = FundedWallet();
        var tooMuch = TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.0002", "1");
        Assert.Throws<InsufficientBalanceException>(() => TestnetPaymentBuilder.Build(manager, coins, transactions, tooMuch));
        var unconfirmed = FundedWallet(confirmed: false);
        Assert.Throws<InsufficientBalanceException>(() => TestnetPaymentBuilder.Build(unconfirmed.Manager, unconfirmed.Coins, unconfirmed.Transactions,
            TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00003", "1")));
    }

    [Fact]
    public void IdentifiesSelfPaymentWithoutCountingItAsChange()
    {
        var (manager, coins, transactions) = FundedWallet();
        var ownAddress = manager.GetNextReceiveKey(LabelsArray.Empty, ScriptPubKeyType.TaprootBIP86).GetAddress(Network.TestNet4);
        var review = TestnetPaymentBuilder.Build(manager, coins, transactions,
            TestnetPaymentRequest.Parse(ownAddress.ToString(), "0.00003", "1"));
        Assert.True(review.PaysOwnAddress);
        Assert.Equal(3000, review.AmountSatoshis);
        Assert.Equal(10_000, review.AmountSatoshis + review.FeeSatoshis + review.ChangeSatoshis);
    }

    [Fact]
    public void UnsignedFactoryResultDoesNotClaimToBeSigned()
    {
        var (manager, coins, transactions) = FundedWallet();
        var factory = new TransactionFactory(Network.TestNet4, manager, coins, transactions);
        var result = factory.BuildTransaction(new TransactionParameters(
            new PaymentIntent(BitcoinAddress.Create(Address(Network.TestNet4), Network.TestNet4).ScriptPubKey, Money.Satoshis(3000)),
            new FeeRate(1m), false, false, null, TryToSign: false, OverrideFeeOverpaymentProtection: false));
        Assert.False(result.Signed);
        Assert.False(result.Psbt.IsAllFinalized());
        Assert.All(result.Transaction.Transaction.Inputs, input =>
        {
            Assert.Equal(0, input.ScriptSig.Length);
            Assert.Equal(0, input.WitScript.PushCount);
        });
    }

    [Fact]
    public void KeepsFeeOverpaymentProtectionAndRejectsDust()
    {
        var (manager, coins, transactions) = FundedWallet(amount: 100_000);
        Assert.Throws<TransactionFeeOverpaymentException>(() => TestnetPaymentBuilder.Build(manager, coins, transactions,
            TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00001", "100")));
        Assert.ThrowsAny<Exception>(() => TestnetPaymentBuilder.Build(manager, coins, transactions,
            TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.000002", "1")));
    }

    [Fact]
    public async Task CannotBuildBeforeSyncOrAfterCancellation()
    {
        var service = new TestnetWalletSync();
        var request = TestnetPaymentRequest.Parse(Address(Network.TestNet4), "0.00001", "1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildPaymentReviewAsync(request, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BuildPaymentReviewAsync(request, cancellation.Token));
    }

    private static (KeyManager Manager, CoinsView Coins, MemoryTransactions Transactions) FundedWallet(
        bool confirmed = true, long amount = 10_000, ScriptPubKeyType inputType = ScriptPubKeyType.Segwit)
    {
        // A recovery passphrase is intentionally unavailable to the unsigned builder.
        var manager = KeyManager.CreateNew(out _, "recovery passphrase", Network.TestNet4);
        var key = manager.GetNextReceiveKey(LabelsArray.Empty, inputType);
        var coin = BitcoinFactory.CreateSmartCoin(key, Money.Satoshis(amount), confirmed);
        return (manager, new CoinsView([coin]), new MemoryTransactions(coin.Transaction));
    }

    private sealed class MemoryTransactions(SmartTransaction transaction) : ITransactionStore
    {
        public bool TryGetTransaction(uint256 hash, [NotNullWhen(true)] out SmartTransaction? sameStx)
        {
            sameStx = transaction.GetHash() == hash ? transaction : null;
            return sameStx is not null;
        }
    }
}
