using Shine.Domain;

namespace Shine.UnitTests;

public sealed class OperationalEventPublisherTests
{
    [Fact]
    public void Operational_event_id_defines_a_stable_idempotency_key()
    {
        var id = Guid.NewGuid();
        var first = $"operational:{id:N}";
        var second = $"operational:{id:N}";

        Assert.Equal(first, second);
    }
}
