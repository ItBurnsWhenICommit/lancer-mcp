using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SimHashServiceTests
{
    [Fact]
    public void Compute_IsDeterministicAndProducesBands()
    {
        var service = new SimHashService();

        var resultA = service.Compute(new[] { "user", "service", "login" });
        var resultB = service.Compute(new[] { "user", "service", "login" });
        var resultC = service.Compute(new[] { "user", "service", "logout" });

        Assert.Equal(resultA.Hash, resultB.Hash);
        Assert.NotEqual(resultA.Hash, resultC.Hash);

        Assert.Equal((int)(resultA.Hash & 0xFFFF), resultA.Band0);
        Assert.Equal((int)((resultA.Hash >> 16) & 0xFFFF), resultA.Band1);
        Assert.Equal((int)((resultA.Hash >> 32) & 0xFFFF), resultA.Band2);
        Assert.Equal((int)((resultA.Hash >> 48) & 0xFFFF), resultA.Band3);
    }
}
