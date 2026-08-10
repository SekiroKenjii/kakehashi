using System;
using System.Collections.Generic;

namespace Kakehashi.SharedKernel {
  /// <summary>Base class for entities, compared by identity rather than by value.</summary>
  /// <typeparam name="TId">The type of the entity identifier.</typeparam>
  public abstract class Entity<TId> : IEquatable<Entity<TId>>
      where TId : notnull {
    private readonly List<IDomainEvent> _domainEvents = new();

    protected Entity(TId id) {
      Id = id;
    }

    public TId Id { get; }

    /// <summary>Domain events raised by this entity that have not yet been dispatched.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) {
      return Equals(left, right);
    }

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) {
      return !Equals(left, right);
    }

    public void ClearDomainEvents() {
      _domainEvents.Clear();
    }

    public bool Equals(Entity<TId>? other) {
      if (other is null) {
        return false;
      }
      if (ReferenceEquals(this, other)) {
        return true;
      }
      return GetType() == other.GetType()
          && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) {
      return obj is Entity<TId> other && Equals(other);
    }

    public override int GetHashCode() {
      return EqualityComparer<TId>.Default.GetHashCode(Id);
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent) {
      _domainEvents.Add(domainEvent);
    }
  }
}
