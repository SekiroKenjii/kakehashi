using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Kakehashi.UI.Common.Controls {
  // One control rather than a copied block of XAML: the header is the fastest place for screens to
  // drift apart, one page ending up with 6px of spacing and another with 8. It lives in
  // Kakehashi.UI.Common because feature modules have pages too, and a control in the host is a
  // control they cannot reference.
  [ContentProperty(Name = nameof(Commands))]
  public sealed partial class PageHeader : UserControl {
    // Defaulted here rather than set per page: making every page repeat the word is an invitation
    // for one of them to spell it differently.
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root), typeof(string), typeof(PageHeader), new PropertyMetadata("Kakehashi"));

    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CommandsProperty = DependencyProperty.Register(
        nameof(Commands), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader() {
      InitializeComponent();
    }

    public string Root {
      get => (string)GetValue(RootProperty);
      set => SetValue(RootProperty, value);
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

    public static Visibility WhenSet(string value) {
      return string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    }

    public static Visibility WhenPresent(object? value) {
      return value is null ? Visibility.Collapsed : Visibility.Visible;
    }
  }
}
