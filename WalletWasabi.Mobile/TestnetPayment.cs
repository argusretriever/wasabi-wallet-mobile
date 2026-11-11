using System;
using System.Globalization;
using System.Linq;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;

namespace WalletWasabi.Mobile;

public sealed record TestnetPaymentRequest
{
    private TestnetPaymentRequest(BitcoinAddress destination, Money amount, FeeRate feeRate)
        => (Destination, Amount, FeeRate) = (destination, amount, feeRate);
    public BitcoinAddress Destination { get; }
    public Money Amount { get; }
    public FeeRate FeeRate { get; }

    public static TestnetPaymentRequest Parse(string address, string amountBtc, string feeSatPerVbyte)
    {
        BitcoinAddress destination;
        try { destination = BitcoinAddress.Create(address.Trim(), Network.TestNet4); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new FormatException("Enter a valid testnet receiving address, without a payment URI."); }
        if (!TryDecimal(amountBtc, out var amount) || amount <= 0 || amount > 21_000_000m ||
            amount * 100_000_000m != decimal.Truncate(amount * 100_000_000m))
            throw new FormatException("Enter a positive BTC amount with no more than 8 decimal places. Use a decimal point.");
        if (!TryDecimal(feeSatPerVbyte, out var fee) || fee < 1 || fee > 1000 || decimal.Round(fee, 3) != fee)
            throw new FormatException("Enter a fee rate from 1 to 1000 sat/vB, with up to 3 decimal places.");
        return new(destination, Money.Coins(amount), new FeeRate(fee));
    }

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}

public sealed record PaymentChange(string Address, long Satoshis);
public sealed record TestnetPaymentReview(string Recipient, long AmountSatoshis, long FeeSatoshis,
    long InputSatoshis, int InputCount, decimal RequestedFeeRate, bool PaysOwnAddress, PaymentChange[] Change)
{
    public long TotalDebitSatoshis => checked(AmountSatoshis + FeeSatoshis);
    public long ChangeSatoshis => Change.Sum(x => x.Satoshis);
}

internal sealed class TestnetPaymentDraft(TestnetPaymentReview review, PSBT psbt)
{
    private readonly string _unsignedPsbt = psbt.ToBase64();
    public TestnetPaymentReview Review { get; } = review;

    public PSBT OpenUnsignedPsbt() => PSBT.Parse(_unsignedPsbt, Network.TestNet4);

    public Transaction Sign(KeyManager manager, ICoinsView coins, string recoveryPassphrase)
    {
        if (manager.GetNetwork() != Network.TestNet4 || manager.IsWatchOnly)
            throw new InvalidOperationException("A Testnet4 software wallet is required.");

        var psbt = OpenUnsignedPsbt();
        var unsigned = psbt.GetGlobalTransaction();
        var inputs = GetAvailableInputs(unsigned, coins);

        var builder = Network.TestNet4.CreateTransactionBuilder()
            .AddCoins(inputs.Select(coin => coin.Coin))
            .AddKeys(manager.GetSecrets(recoveryPassphrase, inputs.Select(coin => coin.ScriptPubKey).ToArray()).ToArray());
        builder.SignPSBT(psbt);
        psbt.Finalize();
        var signed = psbt.ExtractTransaction();
        // The supported native SegWit inputs add witnesses without changing the transaction ID.
        if (signed.GetHash() != unsigned.GetHash() || !builder.Verify(signed))
            throw new InvalidOperationException("The signed transaction failed verification.");
        if (inputs.Any(coin => !coin.Confirmed || !coin.IsAvailable()))
            throw new InvalidOperationException("A reviewed input is no longer available.");
        return signed;
    }

    public void VerifyForBroadcast(Transaction signed, ICoinsView coins)
    {
        if (signed.GetHash() != OpenUnsignedPsbt().GetGlobalTransaction().GetHash() ||
            !Network.TestNet4.CreateTransactionBuilder()
                .AddCoins(GetAvailableInputs(signed, coins).Select(coin => coin.Coin))
                .Verify(signed))
            throw new InvalidOperationException("The transaction does not match the signed review.");
    }

    private static SmartCoin[] GetAvailableInputs(Transaction transaction, ICoinsView coins) =>
        transaction.Inputs.Select(input =>
        {
            if (!coins.TryGetByOutPoint(input.PrevOut, out var coin) || !coin.Confirmed || !coin.IsAvailable())
                throw new InvalidOperationException("A reviewed input is no longer available.");
            return coin;
        }).ToArray();
}

public static class TestnetPaymentBuilder
{
    public static TestnetPaymentReview Build(KeyManager manager, ICoinsView coins,
        ITransactionStore transactions, TestnetPaymentRequest request) =>
        BuildDraft(manager, coins, transactions, request).Review;

    internal static TestnetPaymentDraft BuildDraft(KeyManager manager, ICoinsView coins,
        ITransactionStore transactions, TestnetPaymentRequest request)
    {
        if (manager.GetNetwork() != Network.TestNet4) throw new InvalidOperationException("Testnet4 only.");
        var factory = new TransactionFactory(Network.TestNet4, manager, coins, transactions);
        var parameters = new TransactionParameters(
            new PaymentIntent(request.Destination.ScriptPubKey, request.Amount), request.FeeRate,
            AllowUnconfirmed: false, AllowDoubleSpend: false, AllowedInputs: null,
            TryToSign: false, OverrideFeeOverpaymentProtection: false);
        var result = factory.BuildTransaction(parameters);
        var tx = result.Transaction.Transaction;
        if (tx.Outputs.Any(output => output.IsDust()))
            throw new InvalidOperationException("The transaction contains a dust output.");
        if (result.Signed || result.Psbt.IsAllFinalized() ||
            tx.Inputs.Any(x => x.ScriptSig.Length != 0 || x.WitScript.PushCount != 0))
            throw new InvalidOperationException("Review must contain an unsigned transaction.");
        var recipients = tx.Outputs.Where(x => x.ScriptPubKey == request.Destination.ScriptPubKey).ToArray();
        if (recipients.Length != 1 || recipients[0].Value != request.Amount)
            throw new InvalidOperationException("The transaction does not match the requested payment.");
        var change = tx.Outputs.Where(x => !ReferenceEquals(x, recipients[0])).Select(output =>
        {
            if (!manager.TryGetKeyForScriptPubKey(output.ScriptPubKey, out var key) || !key.IsInternal)
                throw new InvalidOperationException("Unexpected transaction output.");
            return new PaymentChange(output.ScriptPubKey.GetDestinationAddress(Network.TestNet4)!.ToString(), output.Value.Satoshi);
        }).ToArray();
        var inputs = result.SpentCoins.ToArray();
        var inputTotal = inputs.Sum(x => x.Amount.Satoshi);
        if (result.Fee < Money.Zero || inputTotal != request.Amount.Satoshi + result.Fee.Satoshi + change.Sum(x => x.Satoshis))
            throw new InvalidOperationException("Transaction amounts do not balance.");
        var review = new TestnetPaymentReview(request.Destination.ToString(), request.Amount.Satoshi, result.Fee.Satoshi,
            inputTotal, inputs.Length, request.FeeRate.SatoshiPerByte,
            manager.TryGetKeyForScriptPubKey(request.Destination.ScriptPubKey, out _), change);
        return new(review, result.Psbt);
    }
}
