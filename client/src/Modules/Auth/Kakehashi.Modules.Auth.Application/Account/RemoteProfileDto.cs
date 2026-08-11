namespace Kakehashi.Modules.Auth.Application.Account {
  // The user's profile as stored on the authorization server.
  public sealed record RemoteProfileDto(
      string? DisplayName, string Email, string? Phone, bool TwoFactorEnabled);
}
