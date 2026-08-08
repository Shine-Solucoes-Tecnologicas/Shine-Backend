namespace Shine.Domain;

public interface IEntity<out TId>
{
    TId Id { get; }
}

public abstract class BaseEntity<TId>(TId id) : IEntity<TId>
{
    public TId Id { get; protected init; } = id;
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
