using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace __ROOT_NAMESPACE__.UI.Common.Controls;

/// <summary>
/// Hosts content with an optional opacity mask. This is a minimal seam: it exposes an
/// <see cref="OpacityMaskBrush"/> and presents its content. Extend with the Composition APIs
/// (e.g. a <c>CompositionMaskBrush</c>) for true per-pixel alpha masking.
/// </summary>
public sealed partial class OpacityMaskView : ContentControl
{
    public static readonly DependencyProperty OpacityMaskBrushProperty =
        DependencyProperty.Register(
            nameof(OpacityMaskBrush),
            typeof(Brush),
            typeof(OpacityMaskView),
            new PropertyMetadata(null));

    public OpacityMaskView()
    {
        DefaultStyleKey = typeof(ContentControl);
    }

    public Brush? OpacityMaskBrush
    {
        get => (Brush?)GetValue(OpacityMaskBrushProperty);
        set => SetValue(OpacityMaskBrushProperty, value);
    }
}
