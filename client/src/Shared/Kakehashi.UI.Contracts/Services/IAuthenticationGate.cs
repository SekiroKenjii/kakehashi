using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services {
  /// <summary>
  /// A startup gate that must complete before the shell is shown. The host runs every registered
  /// gate during startup; a module (e.g. Auth) contributes one to require sign-in. When no gate is
  /// registered the host simply proceeds, so the gate is entirely optional.
  /// </summary>
  public interface IAuthenticationGate : ISingletonDependency {
    /// <summary>
    /// Ensures the user is authenticated, driving any interactive sign-in required. Returns once the
    /// gate is satisfied; throws to abort startup (the host surfaces the failure).
    /// </summary>
    Task EnsureAuthenticatedAsync(CancellationToken cancellationToken);
  }
}
