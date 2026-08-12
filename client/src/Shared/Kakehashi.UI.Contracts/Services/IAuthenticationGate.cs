using System.Threading;
using System.Threading.Tasks;

namespace Kakehashi.UI.Contracts.Services {
  // Every registered gate must complete before the shell is shown. Registering none is legal - the
  // host simply proceeds - which is what keeps sign-in an optional module.
  public interface IAuthenticationGate : ISingletonDependency {
    // Drives any interactive sign-in required. Returning satisfies the gate; throwing aborts
    // startup and the host surfaces the failure.
    Task EnsureAuthenticatedAsync(CancellationToken cancellationToken);
  }
}
