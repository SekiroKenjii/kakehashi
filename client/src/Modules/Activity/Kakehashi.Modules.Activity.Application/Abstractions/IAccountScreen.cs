namespace Kakehashi.Modules.Activity.Application.Abstractions {
  /// <summary>
  /// Takes somebody to the screen where they can end sessions and change their password.
  /// </summary>
  /// <remarks>
  /// A port for one call, and it earns its place twice over.
  /// <para>
  /// It belongs to another module. The Account page is the Auth module's, and this module may not
  /// reference it, so the only join available is the navigation key its page type derives — a string
  /// with no compile-time check behind it. Keeping that string in one adapter means one place to fix
  /// when that page is renamed, instead of a literal loose in a view model.
  /// </para>
  /// <para>
  /// It is also what makes the behaviour testable. <c>INavigationService.NavigateTo</c> takes a
  /// <c>params ReadOnlySpan</c>, and a ref struct parameter cannot be proxied — a substitute for that
  /// interface produces a method the runtime refuses to run, which takes down not just the test that
  /// calls it but the next test to use a matcher.
  /// </para>
  /// </remarks>
  public interface IAccountScreen {
    /// <summary>
    /// Opens the account screen.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when there is no such screen in this build, which a caller should
    /// answer by saying where to go rather than by doing nothing.
    /// </returns>
    bool Open();
  }
}
