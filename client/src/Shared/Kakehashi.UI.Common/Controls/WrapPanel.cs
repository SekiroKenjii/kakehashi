using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Kakehashi.UI.Common.Controls {
  /// <summary>
  /// Lays children out left to right at their own width, wrapping to the next line when the row
  /// runs out of room.
  /// </summary>
  /// <remarks>
  /// WinUI ships no wrapping panel, and the nearest thing — <c>UniformGridLayout</c> — is uniform by
  /// definition: it measures the first item and gives every cell that width. For chips that is
  /// exactly wrong in both directions. Left to content, a short first chip set the width and a
  /// longer one had its remove button clipped off the end; forced to fill a fixed column count, a
  /// two-letter role stretched across half the panel with its × marooned at the far right.
  /// <para>
  /// A chip is as wide as the word inside it. That is the whole requirement, and it needs a panel
  /// that asks each child what it wants rather than one that decides for all of them.
  /// </para>
  /// <para>
  /// Deliberately not virtualizing. The lists it serves are a handful of roles or tags, and a
  /// virtualizing layout costs a recycling context and a realization window to save nothing.
  /// </para>
  /// </remarks>
  public sealed partial class WrapPanel : Panel {
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
    public double HorizontalSpacing {
      get { return (double)GetValue(HorizontalSpacingProperty); }
      set { SetValue(HorizontalSpacingProperty, value); }
    }

    /// <summary>The gap between two lines.</summary>
    public double VerticalSpacing {
      get { return (double)GetValue(VerticalSpacingProperty); }
      set { SetValue(VerticalSpacingProperty, value); }
    }

    protected override Size MeasureOverride(Size availableSize) {
      // Height is left unbounded while measuring: how tall this ends up is the answer, not a
      // constraint on it. Width is the constraint, because width is what decides where to wrap.
      var childLimit = new Size(availableSize.Width, double.PositiveInfinity);

      double lineWidth = 0;
      double lineHeight = 0;
      double totalWidth = 0;
      double totalHeight = 0;

      foreach (var child in Children) {
        child.Measure(childLimit);
        var desired = child.DesiredSize;

        // A child that does not fit beside what is already on this line starts the next one. The
        // first child on a line always stays there, however wide it is: moving it down would leave
        // an empty line above and still not make it fit.
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
