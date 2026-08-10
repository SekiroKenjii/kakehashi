namespace Kakehashi.Modules.Auth.Application.Account {
  /// <summary>The user's profile as stored on the authorization server.</summary>
  public sealed record RemoteProfileDto(
      string? DisplayName, string Email, string? Phone, bool TwoFactorEnabled);
}
