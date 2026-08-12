using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Kakehashi.UI.Common.Controls {
  // WinUI ships no wrapping panel, and the nearest thing, UniformGridLayout, is uniform by
  // definition: it measures the first item and gives every cell that width. For chips that is wrong
  // in both directions. Left to content, a short first chip set the width and a longer one had its
  // remove button clipped off the end; forced to a fixed column count, a two-letter role stretched
  // across half the panel with its close button marooned at the far right. A chip is as wide as the
  // word inside it, which needs a panel that asks each child what it wants.
  //
  // Deliberately not virtualizing: the lists it serves are a handful of roles or tags, and a
  // virtualizing layout costs a recycling context and a realization window to save nothing.
  public sealed partial class WrapPanel : Panel {
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    public double HorizontalSpacing {
      get { return (double)GetValue(HorizontalSpacingProperty); }
      set { SetValue(HorizontalSpacingProperty, value); }
    }

    public double VerticalSpacing {
      get { return (double)GetValue(VerticalSpacingProperty); }
      set { SetValue(VerticalSpacingProperty, value); }
    }

    protected override Size MeasureOverride(Size availableSize) {
      // Height is unbounded while measuring: how tall this ends up is the answer, not a constraint
      // on it. Width is the constraint, because width is what decides where to wrap.
      var childLimit = new Size(availableSize.Width, double.PositiveInfinity);

      double lineWidth = 0;
      double lineHeight = 0;
      double totalWidth = 0;
      double totalHeight = 0;

      foreach (var child in Children) {
        child.Measure(childLimit);
        var desired = child.DesiredSize;

        // The first child on a line always stays there, however wide it is: moving it down would
        // leave an empty line above and still not make it fit.
        if (lineWidth > 0 && lineWidth + HorizontalSpacing + desired.Width > availableSize.Width) {
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

    protected override Size ArrangeOverride(Size finalSize) {
      double x = 0;
      double y = 0;
      double lineHeight = 0;

      foreach (var child in Children) {
        var desired = child.DesiredSize;

        if (x > 0 && x + desired.Width > finalSize.Width) {
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

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
      ((WrapPanel)sender).InvalidateMeasure();
    }
  }
}
