package checks

import (
	"context"
	"strings"
)

// windowsAppRuntime is what an unpackaged WinUI executable loads at startup. Without it the client
// exits before its first line runs, and so do its test projects.
func windowsAppRuntime() Check {
	return Check{
		Name:    "Windows App Runtime",
		Level:   Advisory,
		Windows: true,
		probe: func(ctx context.Context) (Status, string, string) {
			const fix = "install it with 'winget install Microsoft.WindowsAppRuntime.1.7', " +
				"or from https://aka.ms/windowsappsdk/downloads"
			out, err := output(ctx, "powershell", "-NoProfile", "-NonInteractive", "-Command",
				"(Get-AppxPackage -Name Microsoft.WindowsAppRuntime.* | Select-Object -First 1).Version")
			if err != nil || strings.TrimSpace(out) == "" {
				return Warn, "not detected", fix
			}
			return Pass, strings.TrimSpace(out), ""
		},
	}
}

// developerMode is only needed to deploy an MSIX from a build, which is not the default packaging.
func developerMode() Check {
	return Check{
		Name:    "Windows Developer Mode",
		Level:   Advisory,
		Windows: true,
		probe: func(ctx context.Context) (Status, string, string) {
			const key = `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock`
			out, err := output(ctx, "reg", "query", key, "/v", "AllowDevelopmentWithoutDevLicense")
			if err != nil || !strings.Contains(out, "0x1") {
				return Warn, "off", "needed only to deploy an MSIX build: Settings > System > For developers"
			}
			return Pass, "on", ""
		},
	}
}
