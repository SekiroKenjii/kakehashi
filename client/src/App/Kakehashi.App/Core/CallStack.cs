namespace Kakehashi.App.Core;

internal sealed record CallStack
{
    public string ExceptionType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public CallStackDetail Detail { get; init; } = new();
}

internal sealed record CallStackDetail
{
    public string Module { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public int Line { get; init; }
    public string Address { get; init; } = string.Empty;
}
