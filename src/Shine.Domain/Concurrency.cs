namespace Shine.Domain;

public interface IConcurrencyTracked
{
    Guid Version { get; }
}
