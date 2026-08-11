namespace Shine.Domain;

public interface IEntity<out TId>
{
    TId Id { get; }
}

public interface IDomainEvent { }
public interface IIntegrationEvent
{
    Guid TenantId { get; }
    DateTime OccurredAtUtc { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void AddDomainEvent(IDomainEvent domainEvent);
    IReadOnlyCollection<IDomainEvent> ClearDomainEvents();
}

public abstract class BaseEntity<TId>(TId id) : IEntity<TId>, IHasDomainEvents
{
    public TId Id { get; protected init; } = id;
    private readonly List<IDomainEvent> domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => domainEvents.Add(domainEvent ?? throw new ArgumentNullException(nameof(domainEvent)));
    public IReadOnlyCollection<IDomainEvent> ClearDomainEvents()
    {
        var pending = domainEvents.ToArray();
        domainEvents.Clear();
        return pending;
    }
}

public interface IAggregateRoot { }

public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) => other is not null && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);
    public override int GetHashCode() => GetEqualityComponents().Aggregate(17, (hash, value) => HashCode.Combine(hash, value));
    public static bool operator ==(ValueObject? left, ValueObject? right) => Equals(left, right);
    public static bool operator !=(ValueObject? left, ValueObject? right) => !Equals(left, right);
}

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public interface ICreatable<in TCreation>
{
    void Create(TCreation creation);
}

public interface IUpdatable<in TUpdate>
{
    void Update(TUpdate update);
}
