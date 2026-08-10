using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Kakehashi.UI.Common.Controls {
  /// <summary>
  /// The standard top strip of a page: a breadcrumb on the left, the page's commands on the right.
  /// </summary>
  /// <remarks>
  /// One control rather than a copied block of XAML. The header is the first thing on every screen
  /// and the fastest place for screens to drift apart — one page ends up with 6px of spacing and
  /// another with 8, one puts its refresh button inside the group and another beside it. A control
  /// makes the layout a single decision and leaves each page only the parts that are genuinely its
  /// own: what it is called, and what it can do.
  /// <para>
  /// It lives in <c>Kakehashi.UI.Common</c> because feature modules have pages too, and a control
  /// in the host is a control they cannot reference.
  /// </para>
  /// <para>
  /// Usage — the commands are the content, so they need no property element:
  /// </para>
  /// <code>
  /// &lt;controls:PageHeader Section="Administration" Title="Users"&gt;
  ///   &lt;StackPanel Orientation="Horizontal" Spacing="2"&gt;
  ///     &lt;Button Style="{StaticResource AccentToolbarButtonStyle}" …/&gt;
  ///     &lt;Border Style="{StaticResource CommandBarDividerStyle}"/&gt;
  ///     &lt;Button Style="{StaticResource ToolbarButtonStyle}" …/&gt;
  ///   &lt;/StackPanel&gt;
  /// &lt;/controls:PageHeader&gt;
  /// </code>
  /// </remarks>
  [ContentProperty(Name = nameof(Commands))]
  public sealed partial class PageHeader : UserControl {
    /// <summary>The area this page belongs to — the muted first crumb. Empty hides the crumb.</summary>
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    /// <summary>The page's own name — the bold last crumb.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    /// <summary>The page's actions, shown inside the bordered command group.</summary>
    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(
        nameof(Commands), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader() {
      InitializeComponent();
    }

    public string Section {
      get => (string)GetValue(SectionProperty);
      set => SetValue(SectionProperty, value);
    }

    public string Title {
      get => (string)GetValue(TitleProperty);
      set => SetValue(TitleProperty, value);
    }

    public object? Commands {
      get => GetValue(CommandsProperty);
      set => SetValue(CommandsProperty, value);
    }

    /// <summary>Shows the first crumb and its chevron only when there is a section to show.</summary>
    public static Visibility WhenSet(string value) {
      return string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the command group only when a page contributed commands.</summary>
    public static Visibility WhenPresent(object? value) {
      return value is null ? Visibility.Collapsed : Visibility.Visible;
    }
  }
}
