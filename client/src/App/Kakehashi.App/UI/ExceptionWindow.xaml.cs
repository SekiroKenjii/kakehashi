using System;
using System.Text;
using Kakehashi.App.Extensions;
using Kakehashi.UI.Common.Helpers;
using Microsoft.UI.Xaml;
using WinUIEx;

namespace Kakehashi.App.UI;

/// <summary>
/// A last-resort window shown for unhandled exceptions. It surfaces the error and its inner
/// exceptions and lets the user continue, restart, or exit the app.
/// </summary>
public sealed partial class ExceptionWindow : WindowEx
{
    public ExceptionWindow()
    {
        InitializeComponent();

        WindowHelper.TrySetAppIcon(this);
        ExtendsContentIntoTitleBar = true;
    }

    internal static void ShowException(Exception exception)
    {
        var window = new ExceptionWindow();
        window.Populate(exception);
        window.Activate();
    }

    private void Populate(Exception exception)
    {
        var stacks = exception.ToCallStacks();
        MessageText.Text = stacks.Count > 0
            ? $"{stacks[0].ExceptionType}: {stacks[0].Message}"
            : exception.Message;

        var builder = new StringBuilder();
        foreach (var stack in stacks)
        {
            builder.AppendLine($"{stack.ExceptionType}: {stack.Message}");

            if (!string.IsNullOrEmpty(stack.Detail.Method))
            {
                builder.AppendLine($"  at {stack.Detail.Module} {stack.Detail.Method}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        DetailText.Text = builder.ToString();
    }

    private void OnContinueButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        App.Current.Exit();
    }

    private void OnRestartButtonClick(object sender, RoutedEventArgs e)
    {
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }
}
