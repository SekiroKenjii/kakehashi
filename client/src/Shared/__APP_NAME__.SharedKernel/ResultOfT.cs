using System;

namespace __ROOT_NAMESPACE__.SharedKernel;

/// <summary>The outcome of an operation that returns a <typeparamref name="TValue"/> on success.</summary>
/// <typeparam name="TValue">The type produced when the operation succeeds.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value
    {
        get {
            if (IsFailure)
            {
                throw new InvalidOperationException("The value of a failed result cannot be accessed.");
            }

            return _value!;
        }
    }

    public static implicit operator Result<TValue>(TValue value)
    {
        return Success(value);
    }
}
