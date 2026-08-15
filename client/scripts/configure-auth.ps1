#Requires -Version 7
<#
.SYNOPSIS
  Toggles the optional Auth (OpenID Connect) module for this boilerplate.

.DESCRIPTION
  The Auth module ships enabled but inert (it does nothing until an authority is configured in
  appsettings.json). Run this during project setup with -Remove to strip it out completely: its three
  source projects and three test projects, the solution entries, the host project reference and module
  registration, the NuGet package pins, the architecture-test coverage, and the Auth config section.

  The host's authentication *seams* are intentionally left in place because they are inert without the
  module and keep the build green:
    - IAccessTokenProvider (Application.Abstractions) + the default NullAccessTokenProvider,
    - the BearerTokenHandler / gRPC call credentials in App.Infrastructure,
    - IAuthenticationGate (UI.Contracts) + the AuthenticationOrchestrator in the host.
  With the module gone, no gate is registered (so startup proceeds) and the token provider returns
  null (so backend calls go out unauthenticated). To re-enable the module, revert with git.

.PARAMETER Remove
  Remove the Auth module from the solution.

.PARAMETER SkipBuild
  Skip the verification build at the end.

.EXAMPLE
  pwsh scripts/configure-auth.ps1 -Remove
#>
[CmdletBinding()]
param(
  [switch]$Remove,
  [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

if (-not $Remove) {
  Write-Host 'The Auth module ships enabled. To remove it during setup, run:' -ForegroundColor Yellow
  Write-Host '    pwsh scripts/configure-auth.ps1 -Remove'
  return
}

function Remove-MatchingLines([string]$relativePath, [string]$pattern) {
  $path = Join-Path $root $relativePath
  if (-not (Test-Path $path)) { return }
  $lines = @(Get-Content -LiteralPath $path)
  $kept = @($lines | Where-Object { $_ -notmatch $pattern })
  if ($kept.Count -ne $lines.Count) {
    Set-Content -LiteralPath $path -Value $kept -Encoding utf8
    Write-Host "  updated $relativePath"
  }
}

function Remove-Block([string]$relativePath, [string]$regex) {
  $path = Join-Path $root $relativePath
  if (-not (Test-Path $path)) { return }
  $text = Get-Content -LiteralPath $path -Raw
  $updated = [regex]::Replace($text, $regex, '', 'Singleline')
  if ($updated -ne $text) {
    Set-Content -LiteralPath $path -Value $updated -Encoding utf8 -NoNewline
    Write-Host "  updated $relativePath"
  }
}

Write-Host 'Removing the Auth module...' -ForegroundColor Cyan

# 1. Delete the module source and test projects.
$directories = @(
  'src/Modules/Auth',
  'tests/__APP_NAME__.Modules.Auth.Domain.Tests',
  'tests/__APP_NAME__.Modules.Auth.Application.Tests',
  'tests/__APP_NAME__.Modules.Auth.IntegrationTests'
)
foreach ($directory in $directories) {
  $path = Join-Path $root $directory
  if (Test-Path $path) {
    Remove-Item -LiteralPath $path -Recurse -Force
    Write-Host "  removed $directory"
  }
}

# 2. Delete the Auth architecture-test coverage (kept in its own file for exactly this reason).
$authLayering = Join-Path $root 'tests/__APP_NAME__.ArchitectureTests/AuthLayeringTests.cs'
if (Test-Path $authLayering) {
  Remove-Item -LiteralPath $authLayering -Force
  Write-Host '  removed tests/__APP_NAME__.ArchitectureTests/AuthLayeringTests.cs'
}

# 3. Solution: remove the Auth source-folder block, then the Auth test-project entries.
Remove-Block '__APP_NAME__.slnx' '\s*<Folder Name="/src/Modules/Auth/">.*?</Folder>'
Remove-MatchingLines '__APP_NAME__.slnx' '__APP_NAME__\.Modules\.Auth\.'

# 4. Host: project reference + module registration.
Remove-MatchingLines 'src/App/__APP_NAME__.App/__APP_NAME__.App.csproj' '__APP_NAME__\.Modules\.Auth\.UI'
Remove-MatchingLines 'src/App/__APP_NAME__.App/Composition/ModuleCatalog.cs' '__APP_NAME__\.Modules\.Auth\.UI|new AuthModule\(\)'

# 5. Architecture-test project references.
Remove-MatchingLines 'tests/__APP_NAME__.ArchitectureTests/__APP_NAME__.ArchitectureTests.csproj' '__APP_NAME__\.Modules\.Auth\.'

# 6. Central Package Management entries for the OIDC client + DPAPI.
Remove-Block 'Directory.Packages.props' '\s*<ItemGroup Label="Authentication[^"]*">.*?</ItemGroup>'

# 7. appsettings: drop the Auth section.
$settingsPath = Join-Path $root 'src/App/__APP_NAME__.App/appsettings.json'
if (Test-Path $settingsPath) {
  $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
  if ($settings.ContainsKey('Auth')) {
    $settings.Remove('Auth')
    ($settings | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $settingsPath -Encoding utf8
    Write-Host '  updated src/App/__APP_NAME__.App/appsettings.json'
  }
}

Write-Host 'Auth module removed.' -ForegroundColor Green

if (-not $SkipBuild) {
  Write-Host 'Verifying build...' -ForegroundColor Cyan
  & dotnet build (Join-Path $root '__APP_NAME__.slnx') --nologo
}
