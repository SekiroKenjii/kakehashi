using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.App.UI {
  public sealed partial class ShellViewModel : ViewModel {
    private readonly INavigationService _navigationService;
    private readonly IDisposable _navigationSubscription;

    [ObservableProperty]
    public partial object? Selected { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool CanGoBack { get; set; }

    public ShellViewModel(INavigationService navigationService) {
      ArgumentNullException.ThrowIfNull(navigationService);
      _navigationService = navigationService;
      StatusText = "Ready";
      _navigationSubscription = navigationService.OnNavigated.Subscribe(OnNavigated);
    }

    private void OnNavigated(NavigationEvent e) {
      CanGoBack = _navigationService.CanGoBack;
      StatusText = $"Navigated to {e.SourcePageType.Name}";
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        _navigationSubscription.Dispose();
      }
    }
  }
}
