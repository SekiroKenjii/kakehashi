using Kakehashi.App.Services;
using Xunit;

namespace Kakehashi.App.Tests.Services {
  // The service's two failure behaviours say opposite things: an expired token must DROP the
  // arrangement, any other failure must LEAVE it standing. The wrong way round is invisible in a
  // green build and obvious to a user — one draws a pane for somebody who has signed out, the other
  // blanks the menu because a call timed out.
  //
  // Covered here is the contract between service and planner: the service reports "I have nothing"
  // by being empty, and the planner reads emptiness as "use the arrangement I was built with".
  // Driving the two RpcException branches needs a seam the generated gRPC client does not offer —
  // its methods return AsyncUnaryCall, which no substitute constructs cleanly — so those stay
  // covered by the live verification rather than here.
  public sealed class NavigationLayoutServiceTests {
    [Fact]
    public void NoneIsEmptyAndIsWhatTheServiceStartsWith() {
      Assert.True(NavigationLayout.None.IsEmpty);
      Assert.Empty(NavigationLayout.None.Ungrouped);
      Assert.Empty(NavigationLayout.None.Groups);
    }

    [Fact]
    public void ALayoutWithAnythingInItIsNotEmpty() {
      var withGroup = new NavigationLayout(
          [], [new NavigationGroup("Utilities", [new NavigationPlacement("notes", "N", "", true)])]);
      var withUngrouped = new NavigationLayout(
          [new NavigationPlacement("notes", "N", "", true)], []);

      Assert.False(withGroup.IsEmpty);
      Assert.False(withUngrouped.IsEmpty);
    }

  }
}
