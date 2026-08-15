namespace __ROOT_NAMESPACE__.SharedKernel;

/// <summary>
/// Base class for aggregate roots. An aggregate root is the only member of an aggregate that
/// outside code may hold a reference to, and the unit that repositories load and persist.
/// </summary>
/// <typeparam name="TId">The type of the aggregate identifier.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    protected AggregateRoot(TId id)
        : base(id)
    {
    }
}
