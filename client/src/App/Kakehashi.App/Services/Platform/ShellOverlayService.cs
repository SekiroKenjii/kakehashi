using System;
using Kakehashi.App.Hosting.Orchestration;
using Kakehashi.UI.Contracts.Services.Platform;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kakehashi.App.Services.Platform;

/// <summary>
/// Inserts a blocking, in-app acrylic layer over the main window's content root (including the
/// custom title bar) so the app behind a modal interaction is blurred and inert.
/// </summary>
public sealed class ShellOverlayService : IShellOverlay
{
    private readonly StartupContext _context;

    public ShellOverlayService(StartupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public IDisposable Show()
    {
        if (_context.MainWindow?.Content is not Panel root)
        {
            return EmptyHandle.Instance;
        }

        // In-app acrylic samples the app's own content, which gives the blurred-app look behind the
        // modal; the dark tint keeps it legible in both themes.
        var overlay = new Grid {
            Background = new AcrylicBrush {
                TintColor = Colors.Black,
                TintOpacity = 0.2,
                TintLuminosityOpacity = 0.5,
                FallbackColor = Windows.UI.Color.FromArgb(0xA0, 0x20, 0x20, 0x20),
            },
        };
        root.Children.Add(overlay);
        return new OverlayHandle(root, overlay);
    }

    private sealed class OverlayHandle : IDisposable
    {
        private readonly Panel _root;
        private readonly Grid _overlay;
        private bool _disposed;

        public OverlayHandle(Panel root, Grid overlay)
        {
            _root = root;
            _overlay = overlay;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _root.Children.Remove(_overlay);
        }
    }

    private sealed class EmptyHandle : IDisposable
    {
        public static readonly EmptyHandle Instance = new();

        public void Dispose()
        {
        }
    }
}
