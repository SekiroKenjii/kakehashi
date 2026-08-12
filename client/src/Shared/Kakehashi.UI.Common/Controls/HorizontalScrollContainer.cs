using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Kakehashi.UI.Common.Controls {
  // ScrollViewer is sealed, so this wraps one instead of subclassing it.
  [ContentProperty(Name = nameof(ScrollableContent))]
  public sealed partial class HorizontalScrollContainer : UserControl {
    private readonly ScrollViewer _scrollViewer;

    public HorizontalScrollContainer() {
      _scrollViewer = new ScrollViewer {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
      };
      Content = _scrollViewer;
    }

    public object? ScrollableContent {
      get => _scrollViewer.Content;
      set => _scrollViewer.Content = value;
    }
  }
}
