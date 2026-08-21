using Shine.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Shine.Infrastructure;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IDomainEventDispatcher
{
    Task PublishAsync(IEnumerable<IHasDomainEvents> aggregates, CancellationToken cancellationToken = default);
}

public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task PublishAsync(IEnumerable<IHasDomainEvents> aggregates, CancellationToken cancellationToken = default)
    {
        var sources = aggregates.ToArray();
        var pending = sources.SelectMany(aggregate => aggregate.DomainEvents).ToArray();
        foreach (var domainEvent in pending)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handlers = services.GetServices(handlerType).ToArray();
            foreach (var handler in handlers)
            {
                var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
            }
        }

        foreach (var aggregate in sources) aggregate.ClearDomainEvents();
    }
}
