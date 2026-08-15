using Microsoft.UI.Xaml.Controls;

namespace Kakehashi.UI.Common.Controls;

/// <summary>
/// A clickable card — a semantic <see cref="Button"/> for dashboard-style tiles. Give it a look via
/// a <c>Style</c> in <c>UI/Styles</c>; behavior (command/click) comes from <see cref="Button"/>.
/// </summary>
public sealed partial class Tile : Button
{
    public Tile()
    {
        DefaultStyleKey = typeof(Button);
    }
}
