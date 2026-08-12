using System;
using System.Collections.Generic;
using System.Linq;

namespace Kakehashi.SharedKernel {
  public abstract class ValueObject : IEquatable<ValueObject> {
    public static bool operator ==(ValueObject? left, ValueObject? right) {
      return Equals(left, right);
    }

    public static bool operator !=(ValueObject? left, ValueObject? right) {
      return !Equals(left, right);
    }

    public bool Equals(ValueObject? other) {
      if (other is null) {
        return false;
      }
      if (ReferenceEquals(this, other)) {
        return true;
      }
      return GetType() == other.GetType()
          && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) {
      return obj is ValueObject other && Equals(other);
    }

    public override int GetHashCode() {
      var hash = default(HashCode);
      foreach (var component in GetEqualityComponents()) {
        hash.Add(component);
      }
      return hash.ToHashCode();
    }

    protected abstract IEnumerable<object?> GetEqualityComponents();
  }
}
