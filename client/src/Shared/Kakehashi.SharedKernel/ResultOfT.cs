using System;

namespace Kakehashi.SharedKernel {
  public sealed class Result<TValue> : Result {
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) {
      _value = value;
    }

    public TValue Value {
      get {
        if (IsFailure) {
          throw new InvalidOperationException("The value of a failed result cannot be accessed.");
        }
        return _value!;
      }
    }

    public static implicit operator Result<TValue>(TValue value) {
      return Success(value);
    }
  }
}
