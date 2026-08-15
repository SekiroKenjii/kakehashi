namespace Kakehashi.Application.Abstractions.Messaging;

/// <summary>Represents a void return value for commands that produce no result.</summary>
public readonly record struct Unit
{
    /// <summary>The single <see cref="Unit"/> value.</summary>
    public static readonly Unit Value = default;
}
