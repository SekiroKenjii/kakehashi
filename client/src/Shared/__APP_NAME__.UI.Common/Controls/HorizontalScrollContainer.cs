using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace __ROOT_NAMESPACE__.UI.Common.Controls;

/// <summary>
/// Hosts content in a horizontal-only <see cref="ScrollViewer"/>. <see cref="ScrollViewer"/> is
/// sealed and cannot be subclassed, so this wraps one and exposes its content as the XAML content
/// property.
/// </summary>
[ContentProperty(Name = nameof(ScrollableContent))]
public sealed partial class HorizontalScrollContainer : UserControl
{
    private readonly ScrollViewer _scrollViewer;

    public HorizontalScrollContainer()
    {
        _scrollViewer = new ScrollViewer {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        Content = _scrollViewer;
    }

    /// <summary>The content shown inside the horizontal scroll viewer.</summary>
    public object? ScrollableContent
    {
        get => _scrollViewer.Content;
        set => _scrollViewer.Content = value;
    }
}
