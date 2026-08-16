using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace __ROOT_NAMESPACE__.UI.Common.Controls;

/// <summary>
/// Lays children out left to right at their own width, wrapping to the next line when the row
/// runs out of room.
/// </summary>
/// <remarks>
/// Measures each child at its own width and does not virtualize, so it is wrong for large
/// collections: docs/adr/0012-wrappanel-over-uniformgridlayout.md
/// </remarks>
public sealed partial class WrapPanel : Panel
{
    /// <summary>Identifies the <see cref="HorizontalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    /// <summary>Identifies the <see cref="VerticalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    /// <summary>The gap between two children on the same line.</summary>
    public double HorizontalSpacing
    {
        get { return (double)GetValue(HorizontalSpacingProperty); }
        set { SetValue(HorizontalSpacingProperty, value); }
    }

    /// <summary>The gap between two lines.</summary>
    public double VerticalSpacing
    {
        get { return (double)GetValue(VerticalSpacingProperty); }
        set { SetValue(VerticalSpacingProperty, value); }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Height is left unbounded while measuring: how tall this ends up is the answer, not a
        // constraint on it. Width is the constraint, because width is what decides where to wrap.
        var childLimit = new Size(availableSize.Width, double.PositiveInfinity);

        double lineWidth = 0;
        double lineHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(childLimit);
            var desired = child.DesiredSize;

            // A child that does not fit beside this line starts the next. The first on a line stays,
            // however wide: moving it down leaves an empty line above and still does not make it fit.
            if (lineWidth > 0 && lineWidth + HorizontalSpacing + desired.Width > availableSize.Width)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += lineWidth > 0 ? HorizontalSpacing + desired.Width : desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        ((WrapPanel)sender).InvalidateMeasure();
    }
}
