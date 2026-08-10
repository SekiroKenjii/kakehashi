namespace Kakehashi.App.Core {
  /// <summary>
  /// Represents a call stack, which includes the exception type, message, and details of the call stack. This information is useful for debugging and understanding the context of an exception or error that occurred in the application.
  /// </summary>
  internal sealed record CallStack {
    public string ExceptionType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public CallStackDetail Detail { get; init; } = new();
  }

  /// <summary>
  /// Represents the details of a call stack, including the module, method, file, line number, and address. This information is useful for debugging and understanding the context of an exception or error that occurred in the application.
  /// </summary>
  internal sealed record CallStackDetail {
    public string Module { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public int Line { get; init; }
    public string Address { get; init; } = string.Empty;
  }
}
