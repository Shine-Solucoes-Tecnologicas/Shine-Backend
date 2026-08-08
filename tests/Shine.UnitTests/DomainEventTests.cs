using Shine.Domain;

namespace Shine.UnitTests;

public sealed class DomainEventTests
{
    [Fact]
    public void Aggregate_records_and_clears_events()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        var domainEvent = new TestEvent();

        aggregate.AddDomainEvent(domainEvent);
        var published = aggregate.ClearDomainEvents();

        Assert.Single(published);
        Assert.Same(domainEvent, published.Single());
        Assert.Empty(aggregate.DomainEvents);
    }

    private sealed class TestAggregate(Guid id) : BaseEntity<Guid>(id);
    private sealed class TestEvent : IDomainEvent;
}
