using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kakehashi.App.UI {
  // Static functions rather than converters: x:Bind compile-checks a function call and cannot
  // check a converter. Brushes are cached because these run once per row per render, and a fresh
  // brush per call is garbage for no benefit.
  public static class AdminFormat {
    private static readonly Dictionary<string, SolidColorBrush> _brushes = [];

    public static string Initials(string name) {
      var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 0) {
        return "?";
      }
      if (parts.Length == 1) {
        return char.ToUpperInvariant(parts[0][0]).ToString();
      }
      return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    // Null — never — is the caller's to phrase: the list says "—" where the status column says
    // "Never signed in".
    public static string Relative(DateTimeOffset at) {
      var age = DateTimeOffset.Now - at;
      if (age < TimeSpan.FromMinutes(2)) {
        return "Just now";
      }
      if (age < TimeSpan.FromHours(1)) {
        return $"{(int)age.TotalMinutes} min ago";
      }
      if (age < TimeSpan.FromHours(2)) {
        return "1 hour ago";
      }
      if (age < TimeSpan.FromHours(24)) {
        return $"{(int)age.TotalHours} hours ago";
      }
      if (age < TimeSpan.FromHours(48)) {
        return "Yesterday";
      }
      if (age < TimeSpan.FromDays(14)) {
        return $"{(int)age.TotalDays} days ago";
      }
      return at.ToString("yyyy-MM-dd");
    }

    public static SolidColorBrush RoleForeground(string roleName) {
      return Cached("fg:" + roleName, () => RoleColor(roleName));
    }

    // The same hue at low alpha, so it works on both themes without a second palette.
    public static SolidColorBrush RoleBackground(string roleName) {
      return Cached("bg:" + roleName, () => WithAlpha(RoleColor(roleName), 0x26));
    }

    private static Color RoleColor(string roleName) {
      return roleName switch {
        "Admin" => Color.FromArgb(0xFF, 0xE8, 0x5A, 0x4F),
        "Developer" => Color.FromArgb(0xFF, 0x4C, 0xA0, 0xE0),
        "Operations" => Color.FromArgb(0xFF, 0x3F, 0xB5, 0x50),
        "Viewer" => Color.FromArgb(0xFF, 0xA1, 0x7B, 0xD6),
        "Guest" => Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E),
        _ => Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A),
      };
    }

    private static Color WithAlpha(Color c, byte alpha) {
      return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    private static SolidColorBrush Cached(string key, Func<Color> make) {
      if (!_brushes.TryGetValue(key, out var brush)) {
        brush = new SolidColorBrush(make());
        _brushes[key] = brush;
      }
      return brush;
    }
  }
}
