using System.Linq;
using Kakehashi.Modules.Notes.Domain.Notes;
using Xunit;

namespace Kakehashi.Modules.Notes.Domain.Tests {
  // The client's copy of the server's note rules, duplicated on purpose: this one is for immediate
  // feedback, the server's is the one that decides. These tests keep the copy faithful.
  public sealed class NoteDraftTests {
    [Fact]
    public void Create_TrimsTheTitle() {
      var result = NoteDraft.Create("  Shopping list  ", "milk");

      Assert.True(result.IsSuccess);
      Assert.Equal("Shopping list", result.Value.Title);
      Assert.Equal("milk", result.Value.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Create_RejectsATitleThatIsOnlySpace(string? title) {
      var result = NoteDraft.Create(title, "body");

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.TitleRequired, result.Error);
    }

    [Fact]
    public void Create_TreatsANullBodyAsEmpty() {
      // A note is a title with optional contents.
      var result = NoteDraft.Create("Title", body: null);

      Assert.True(result.IsSuccess);
      Assert.Equal(string.Empty, result.Value.Body);
    }

    [Fact]
    public void Create_AcceptsATitleAtExactlyTheLimit() {
      var result = NoteDraft.Create(new string('a', NoteDraft.MaxTitleLength), string.Empty);

      Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_RejectsATitleOverTheLimit() {
      var result = NoteDraft.Create(new string('a', NoteDraft.MaxTitleLength + 1), string.Empty);

      Assert.True(result.IsFailure);
      Assert.Equal(NotesErrors.TitleTooLong, result.Error);
    }

    [Fact]
    public void Create_CountsTextElementsNotCharacters() {
      // "e" followed by U+0301 COMBINING ACUTE ACCENT: one letter on screen, two UTF-16 chars.
      // Counting chars would reject 120 such letters that the server accepts — a bug that only
      // ever shows up in one language.
      const string composed = "é";
      var title = string.Concat(Enumerable.Repeat(composed, NoteDraft.MaxTitleLength));

      // The premise of the test: if this stops holding, the assertion below proves nothing.
      Assert.Equal(NoteDraft.MaxTitleLength * 2, title.Length);

      var result = NoteDraft.Create(title, string.Empty);

      Assert.True(result.IsSuccess);
    }
  }
}
