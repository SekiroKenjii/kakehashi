using __ROOT_NAMESPACE__.UI.Common.Helpers;
using WinUIEx;

namespace __ROOT_NAMESPACE__.App.UI;

public sealed partial class SplashWindow : WindowEx
{
    public SplashWindow(SplashViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();

        this.CenterOnScreen();
        WindowHelper.TrySetAppIcon(this);
    }

    public SplashViewModel ViewModel { get; }
}
