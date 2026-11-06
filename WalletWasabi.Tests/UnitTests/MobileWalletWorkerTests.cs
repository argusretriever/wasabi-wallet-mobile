using System;
using System.Threading.Tasks;
using WalletWasabi.Services;
using Xunit;

namespace WalletWasabi.Tests.UnitTests;

public class MobileWalletWorkerTests
{
    [Fact]
    public void DisposedWorkerNameCanBeReusedWithoutRemovingReplacement()
    {
        var name = Guid.NewGuid().ToString();
        var first = Workers.Spawn<int>(name, (_, token) => Task.Delay(-1, token));
        Assert.Throws<ArgumentException>(() => Workers.Spawn<int>(name, (_, _) => Task.CompletedTask));
        Assert.True(Workers.Tell(name, 1));
        first.Dispose();
        Assert.False(Workers.Tell(name, 1));
        using var replacement = Workers.Spawn<int>(name, (_, token) => Task.Delay(-1, token));
        first.Dispose();
        Assert.True(Workers.Tell(name, 2));
    }
}
