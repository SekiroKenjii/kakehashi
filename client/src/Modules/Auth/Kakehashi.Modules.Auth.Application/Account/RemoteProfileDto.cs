namespace Kakehashi.Modules.Auth.Application.Account {
  public sealed record RemoteProfileDto(
      string? DisplayName, string Email, string? Phone, bool TwoFactorEnabled);
}
