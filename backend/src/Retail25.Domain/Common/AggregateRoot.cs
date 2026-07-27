namespace Retail25.Domain.Common;

/// <summary>
/// Consistency boundary. One aggregate is saved per unit of work; effects on other aggregates
/// travel as domain events through the transactional outbox.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Something that has happened, expressed in the past tense. Dispatched after the transaction
/// that produced it commits, so a handler can never observe uncommitted state.
/// </summary>
public interface IDomainEvent
{
    Guid EventId => Guid.NewGuid();
    DateTimeOffset OccurredAt { get; }
}
