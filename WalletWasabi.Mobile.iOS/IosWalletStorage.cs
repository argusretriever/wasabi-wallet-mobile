using System;
using System.IO;
using System.Threading.Tasks;
using Foundation;
using Security;

namespace WalletWasabi.Mobile.iOS;

public sealed class IosWalletStorage : IDeviceWalletStorage
{
    private static SecRecord Query() => new(SecKind.GenericPassword)
    {
        Service = "io.github.argusretriever.wasabiwalletmobile.testnet4",
        Account = "wallet.v1"
    };

    public bool Exists
    {
        get
        {
            using var query = Query();
            using var item = SecKeyChain.QueryAsRecord(query, out var status);
            return status == SecStatusCode.ItemNotFound ? false
                : status == SecStatusCode.Success ? true
                : throw new IOException("Keychain is unavailable.");
        }
    }

    public Task SaveNewAsync(byte[] payload) => Task.Run(() =>
    {
        using var item = Query();
        using var data = NSData.FromArray(payload);
        item.ValueData = data;
        item.Accessible = SecAccessible.WhenUnlockedThisDeviceOnly;
        item.Synchronizable = false;
        if (SecKeyChain.Add(item) != SecStatusCode.Success)
            throw new IOException("Could not save wallet in Keychain.");
    });

    public Task<byte[]> ReadAsync() => Task.Run(() =>
    {
        using var query = Query();
        using var item = SecKeyChain.QueryAsRecord(query, out var status);
        if (status != SecStatusCode.Success || item?.ValueData is not { } data)
            throw new IOException("Protected wallet is unavailable.");
        return data.ToArray();
    });
}
