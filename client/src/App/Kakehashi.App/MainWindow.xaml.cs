using System;
using Kakehashi.App.UI;
using Kakehashi.UI.Common.Helpers;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace Kakehashi.App;

/// <summary>
/// Hosts the <see cref="ShellPage"/> and registers the shell's custom title bar; lifecycle is
/// driven by the startup orchestrators.
/// </summary>
public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        this.CenterOnScreen();
        WindowHelper.TrySetAppIcon(this);

        Closed += OnMainWindowClosed;
    }

    internal void AttachShell(ShellPage shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        ExtendsContentIntoTitleBar = true;
        Root.Children.Add(shell);
        SetTitleBar(shell.TitleBar);
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs e)
    {
        await App.ShutdownAsync();
    }
}
