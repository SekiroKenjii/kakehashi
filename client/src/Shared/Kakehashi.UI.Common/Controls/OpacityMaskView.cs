using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kakehashi.UI.Common.Controls {
  // A seam only: the brush is exposed but nothing applies it yet. True per-pixel alpha masking
  // needs the Composition APIs (CompositionMaskBrush).
  public sealed partial class OpacityMaskView : ContentControl {
    public static readonly DependencyProperty OpacityMaskBrushProperty =
        DependencyProperty.Register(
            nameof(OpacityMaskBrush),
            typeof(Brush),
            typeof(OpacityMaskView),
            new PropertyMetadata(null));

    public OpacityMaskView() {
      DefaultStyleKey = typeof(ContentControl);
    }

    public Brush? OpacityMaskBrush {
      get => (Brush?)GetValue(OpacityMaskBrushProperty);
      set => SetValue(OpacityMaskBrushProperty, value);
    }
  }
}
