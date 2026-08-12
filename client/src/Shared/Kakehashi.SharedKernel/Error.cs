namespace Kakehashi.SharedKernel {
  // Code is a stable, dotted identifier such as Notes.Note.TitleRequired; Message is for developers
  // and logs, not for display.
  public sealed record Error(string Code, string Message) {
    public static readonly Error None = new(string.Empty, string.Empty);
  }
}
