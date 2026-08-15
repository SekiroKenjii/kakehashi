namespace Kakehashi.Modules.Activity.Application.Abstractions;

/// <summary>
/// Navigates to the screen where sessions can be ended and the password changed.
/// </summary>
/// <remarks>
/// The Account page belongs to the Auth module, which this module may not reference; the only
/// join is the navigation key its page type derives — a string with no compile-time check — kept
/// in one adapter. The port also keeps tests off <c>INavigationService.NavigateTo</c>, whose
/// <c>params ReadOnlySpan</c> parameter cannot be proxied by a substitute.
/// </remarks>
public interface IAccountScreen
{
    /// <returns>
    /// <see langword="false"/> when there is no such screen in this build; the caller should say
    /// where to go rather than do nothing.
    /// </returns>
    bool Open();
}
