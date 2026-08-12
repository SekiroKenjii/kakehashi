namespace Kakehashi.Modules.Activity.Application.Abstractions {
  // The Account page is the Auth module's, and this module may not reference it, so the only join
  // is the navigation key its page type derives — a string with nothing checking it. Keeping that
  // string in one adapter means one place to fix when the page is renamed.
  //
  // It is also what makes the behaviour testable: INavigationService.NavigateTo takes a
  // params ReadOnlySpan, and a ref struct parameter cannot be proxied, so a substitute for that
  // interface produces a method the runtime refuses to run — taking down both the test that calls
  // it and the next test to use a matcher.
  public interface IAccountScreen {
    // False when this build has no such screen; the caller answers by saying where to go rather
    // than by doing nothing.
    bool Open();
  }
}
