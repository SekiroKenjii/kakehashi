namespace Kakehashi.SharedKernel {
  // The only member of an aggregate outside code may hold a reference to, and the unit repositories
  // load and persist.
  public abstract class AggregateRoot<TId> : Entity<TId>
      where TId : notnull {
    protected AggregateRoot(TId id)
        : base(id) {
    }
  }
}
