using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Mobile;

public interface IBiometricUnlock
{
    bool IsAvailable { get; }
    bool IsEnabled { get; }
    Task EnableAsync(string password, CancellationToken cancellationToken);
    Task<string> ReadPasswordAsync(CancellationToken cancellationToken);
    void Disable();
}
