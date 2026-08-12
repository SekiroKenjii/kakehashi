using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.UI.Common.Controls {
  // Exists only so dashboard tiles can be styled apart from ordinary buttons (see UI/Styles).
  public sealed partial class Tile : Button {
    public Tile() {
      DefaultStyleKey = typeof(Button);
    }
  }
}
