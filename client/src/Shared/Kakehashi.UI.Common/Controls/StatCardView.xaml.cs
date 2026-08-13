using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kakehashi.UI.Common.Controls {
  /// <summary>One count along the top of a screen: a coloured icon square, a label, a number.</summary>
  /// <remarks>
  /// A <c>UserControl</c> in <c>Kakehashi.UI.Common</c> rather than a shared <c>DataTemplate</c>,
  /// like <see cref="PageHeader"/>, so feature modules — compiled into their own assemblies — can
  /// reference it; each screen wraps it in its own short item template.
  /// </remarks>
  public sealed partial class StatCardView : UserControl {
    /// <summary>The card to draw. Null draws an empty card rather than throwing.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card), typeof(StatCard), typeof(StatCardView), new PropertyMetadata(null));

    public StatCardView() {
      InitializeComponent();
    }

    public StatCard? Card {
      get => (StatCard?)GetValue(CardProperty);
      set => SetValue(CardProperty, value);
    }

    /// <summary>
    /// The icon square's colour, per card kind.
    /// </summary>
    /// <remarks>
    /// Four fixed colours carry meaning (green healthy, grey dormant, amber warning, red wrong)
    /// and deliberately do not follow the theme; <see cref="StatKind.Accent"/> carries no meaning,
    /// so it takes the app accent brush.
    /// </remarks>
    public static Brush BrushFor(StatCard? card) {
      return card?.Kind switch {
        StatKind.Positive => _positive,
        StatKind.Muted => _muted,
        StatKind.Warning => _warning,
        StatKind.Critical => _critical,
        // Fully qualified: a bare Application can bind to a Kakehashi.Application namespace rather
        // than to the XAML one, depending on what the consuming assembly has in scope.
        _ => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
            "AccentFillColorDefaultBrush"],
      };
    }

    private static readonly SolidColorBrush _positive =
        new(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x44));
    private static readonly SolidColorBrush _muted =
        new(Color.FromArgb(0xFF, 0x76, 0x76, 0x76));
    private static readonly SolidColorBrush _warning =
        new(Color.FromArgb(0xFF, 0xF2, 0x9A, 0x4D));
    private static readonly SolidColorBrush _critical =
        new(Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
  }
}
