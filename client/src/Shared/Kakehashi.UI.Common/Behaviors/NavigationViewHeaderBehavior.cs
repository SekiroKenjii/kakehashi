using System;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Xaml.Interactivity;

namespace Kakehashi.UI.Common.Behaviors {
  public enum NavigationViewHeaderMode {
    Always,
    Never,
    Minimal
  }

  /// <summary>
  /// Keeps the <see cref="NavigationView"/> header in sync with the current page. It reacts to
  /// navigation through <see cref="INavigationService.OnNavigated"/> and supports per-page header
  /// content, templates and modes via attached properties.
  /// </summary>
  /// <remarks>
  /// The XAML runtime constructs behaviors via <c>new()</c>, so this type cannot receive its
  /// dependencies through constructor injection. Instead it resolves the (singleton) navigation
  /// service and the per-attachment subscription sink from <see cref="ContractServices"/> in
  /// <see cref="OnAttached"/> - i.e. on the very instance XAML created and attached - which is what
  /// makes the singleton lifetimes line up. Configure <see cref="ContractServices"/> once at startup.
  /// </remarks>
  public class NavigationViewHeaderBehavior : Behavior<NavigationView> {
    private static NavigationViewHeaderBehavior? _current;
    private Page? _currentPage;
    private ISubscription? _subscription;

    public DataTemplate? DefaultHeaderTemplate { get; set; }

    public object DefaultHeader {
      get => GetValue(DefaultHeaderProperty);
      set => SetValue(DefaultHeaderProperty, value);
    }

    public static readonly DependencyProperty DefaultHeaderProperty =
        DependencyProperty.Register(
          nameof(DefaultHeader),
          typeof(object),
          typeof(NavigationViewHeaderBehavior),
          new PropertyMetadata(null, (d, e) => _current?.UpdateHeader()));

    public static readonly DependencyProperty HeaderTemplateProperty =
      DependencyProperty.RegisterAttached(
        "HeaderTemplate",
        typeof(DataTemplate),
        typeof(NavigationViewHeaderBehavior),
        new PropertyMetadata(null, (d, e) => _current?.UpdateHeaderTemplate()));

    public static DataTemplate GetHeaderTemplate(Page page) {
      return (DataTemplate)page.GetValue(HeaderTemplateProperty);
    }

    public static void SetHeaderTemplate(Page page, DataTemplate value) {
      page.SetValue(HeaderTemplateProperty, value);
    }

    public static readonly DependencyProperty HeaderModeProperty =
      DependencyProperty.RegisterAttached(
        "HeaderMode",
        typeof(NavigationViewHeaderMode),
        typeof(NavigationViewHeaderBehavior),
        new PropertyMetadata(NavigationViewHeaderMode.Always, (d, e) => _current?.UpdateHeader()));

    public static NavigationViewHeaderMode GetHeaderMode(Page page) {
      return (NavigationViewHeaderMode)page.GetValue(HeaderModeProperty);
    }

    public static void SetHeaderMode(Page page, NavigationViewHeaderMode value) {
      page.SetValue(HeaderModeProperty, value);
    }

    public static readonly DependencyProperty HeaderContextProperty =
      DependencyProperty.RegisterAttached(
        "HeaderContext",
        typeof(object),
        typeof(NavigationViewHeaderBehavior),
        new PropertyMetadata(null, (d, e) => _current?.UpdateHeader()));

    public static object GetHeaderContext(Page page) {
      return page.GetValue(HeaderContextProperty);
    }

    public static void SetHeaderContext(Page page, object value) {
      page.SetValue(HeaderContextProperty, value);
    }

    protected override void OnAttached() {
      base.OnAttached();

      var navigationService = ContractServices.Provider.GetRequiredService<INavigationService>();
      _subscription = ContractServices.Provider.GetRequiredService<ISubscription>();

      _subscription.Add(navigationService.OnNavigated.Subscribe(OnNavigated));
      _current = this;
    }

    protected override void OnDetaching() {
      base.OnDetaching();

      if (_subscription is { Unsubscribed: false }) {
        _subscription.Unsubscribe();
        _subscription = null;
      }

      if (ReferenceEquals(_current, this)) {
        _current = null;
      }
    }

    private void OnNavigated(NavigationEvent e) {
      if (e.Content is not Page page) {
        return;
      }

      _currentPage = page;
      UpdateHeader();
      UpdateHeaderTemplate();
    }

    private void UpdateHeader() {
      if (_currentPage is null) {
        return;
      }

      var headerMode = GetHeaderMode(_currentPage);
      if (headerMode == NavigationViewHeaderMode.Never) {
        AssociatedObject.Header = null;
        AssociatedObject.AlwaysShowHeader = false;
      } else {
        AssociatedObject.Header = GetHeaderContext(_currentPage) ?? DefaultHeader;
        AssociatedObject.AlwaysShowHeader = headerMode == NavigationViewHeaderMode.Always;
      }
    }

    private void UpdateHeaderTemplate() {
      if (_currentPage is null) {
        return;
      }

      AssociatedObject.HeaderTemplate = GetHeaderTemplate(_currentPage) ?? DefaultHeaderTemplate;
    }
  }
}
