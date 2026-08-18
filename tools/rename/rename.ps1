<#
.SYNOPSIS
Turns this template into a project.

.DESCRIPTION
Drops the template's own scaffolding, substitutes every placeholder, renames every path that holds
one, and refuses to finish while a placeholder or an identity string survives.

rename.sh is the same algorithm for Linux, where the server half of CI runs. Both are the reference
the Phase 2 CLI ports to Go: no shell-specific cleverness, one pass per step.

.EXAMPLE
./tools/rename/rename.ps1 -AppName OrderDesk -GoModule github.com/me/orderdesk
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppName,
    [Parameter(Mandatory)][string]$GoModule,
    [string]$AppTitle,
    [string]$ProtoPackage,
    [string]$RootNamespace,
    [string]$Accent = '#C4513C',
    [string]$Author,
    [string]$Year
)

$ErrorActionPreference = 'Stop'

function Die([string]$message) {
    Write-Error "rename: $message"
    exit 1
}

$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

# ── 1. validate and derive ─────────────────────────────────────────────────────────────────────
# The regexes are docs/pivot/02-PHASE-1-TEMPLATIZATION.md §1.
if ($AppName -cnotmatch '^[A-Z][A-Za-z0-9]{1,39}$') {
    Die "-AppName must match ^[A-Z][A-Za-z0-9]{1,39}$, got '$AppName'"
}
if ($GoModule -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._~/-]*[a-zA-Z0-9]$') {
    Die "-GoModule is not a valid module path: '$GoModule'"
}

$appNameLower = $AppName.ToLowerInvariant()
$appNameUpper = $AppName.ToUpperInvariant()
if (-not $AppTitle) { $AppTitle = $AppName }
if (-not $ProtoPackage) { $ProtoPackage = $appNameLower }
if (-not $RootNamespace) { $RootNamespace = $AppName }
if (-not $Author) {
    $Author = (git config user.name 2>$null)
    if (-not $Author) { $Author = $AppName }
}
if (-not $Year) { $Year = (Get-Date).ToUniversalTime().Year.ToString() }

if ($ProtoPackage -cnotmatch '^[a-z][a-z0-9_]*$') {
    Die "-ProtoPackage must match ^[a-z][a-z0-9_]*$, got '$ProtoPackage'"
}
if ($Accent -notmatch '^#[0-9A-Fa-f]{6}$') {
    Die "-Accent must be a six-digit hex colour, got '$Accent'"
}
if ($RootNamespace -cnotmatch '^[A-Z][A-Za-z0-9.]*$') {
    Die "-RootNamespace is not a valid C# namespace: '$RootNamespace'"
}
if ($Year -notmatch '^[0-9]{4}$') { Die "-Year must be four digits, got '$Year'" }

# Longest placeholder first: __APP_NAME_LOWER__ starts with __APP_NAME_, so substituting the short
# one first would leave "OrderDeskLOWER__" behind. An ordered list, not a hashtable, because a
# hashtable does not promise an order.
$placeholders = @(
    @{ Name = '__APP_NAME_LOWER__'; Value = $appNameLower }
    @{ Name = '__APP_NAME_UPPER__'; Value = $appNameUpper }
    @{ Name = '__APP_NAME__'; Value = $AppName }
    @{ Name = '__APP_TITLE__'; Value = $AppTitle }
    @{ Name = '__ROOT_NAMESPACE__'; Value = $RootNamespace }
    @{ Name = '__PROTO_PACKAGE__'; Value = $ProtoPackage }
    @{ Name = '__GO_MODULE__'; Value = $GoModule }
    @{ Name = '__ACCENT__'; Value = $Accent }
    @{ Name = '__AUTHOR__'; Value = $Author }
    @{ Name = '__YEAR__'; Value = $Year }
)

Write-Host "rename: $AppName <$GoModule>"

# ── 2. drop what belongs to the template ───────────────────────────────────────────────────────
# These document the boilerplate and name it on every page. templates/units survives: the
# scaffolded project reads it to remove the example module later. The list is the `exclude` array
# of templates/template.json, which is what the CLI reads instead of this.
$templateOnly = @(
    'docs/BOILERPLATE.md'
    'docs/pivot'
    'docs/brand'
    'docs/adr/0016-one-example-module-in-the-template.md'
    'docs/adr/0017-oidc-provider-is-core.md'
    'docs/adr/0018-database-driven-navigation-stays.md'
    'docs/adr/0019-cli-lives-in-the-monorepo.md'
    'docs/adr/0020-no-second-example-module.md'
    'docs/adr/0022-cli-tags-carry-the-module-path.md'
    'docs/RELEASING.md'
    'tools/cli'
    'tools/inventory'
    'tools/units'
    'packaging'
    'templates/template.json'
    'CHANGELOG.template.md'
    'CHANGELOG.cli.md'
    '.github/ISSUE_TEMPLATE'
    '.github/workflows/scaffold-smoke.yml'
    '.github/workflows/release-template.yml'
    '.github/workflows/release-cli.yml'
)
foreach ($path in $templateOnly) {
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}

function Remove-Lines([string]$File, [string[]]$Patterns) {
    $kept = Get-Content -LiteralPath $File | Where-Object {
        $line = $_
        -not ($Patterns | Where-Object { $line -like "*$_*" })
    }
    Set-Content -LiteralPath $File -Value $kept -NoNewline:$false
}
Remove-Lines 'docs/adr/README.md' @(
    '0016-one-example-module', '0017-oidc-provider', '0018-database-driven-navigation',
    '0019-cli-lives-in-the-monorepo', '0020-no-second-example-module',
    '0022-cli-tags-carry-the-module-path')
Remove-Lines 'README.md' @('docs/brand')

# The scaffold README is the project's. The template's own is about the template.
if (Test-Path 'templates/README.scaffold.md') {
    Move-Item -Force 'templates/README.scaffold.md' 'README.md'
}

# Same for the licence: the root one is this repository's, and names its author.
if (Test-Path 'templates/LICENSE.scaffold') {
    Move-Item -Force 'templates/LICENSE.scaffold' 'LICENSE'
}

# ── 3. content ─────────────────────────────────────────────────────────────────────────────────
$substituted = 0
foreach ($file in (git ls-files)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    # These scripts hold the placeholder table itself. rename.sh is also read from disk as it runs.
    if ($file -like 'tools/rename/*') { continue }

    $bytes = [System.IO.File]::ReadAllBytes($file)
    # A NUL byte means an image or an icon, which no substitution reaches.
    if ([Array]::IndexOf($bytes, [byte]0) -ge 0) { continue }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text -cnotmatch '__[A-Z][A-Z0-9_]*__') { continue }

    foreach ($p in $placeholders) { $text = $text.Replace($p.Name, $p.Value) }
    # No BOM and LF endings, matching .gitattributes.
    [System.IO.File]::WriteAllText($file, $text, (New-Object System.Text.UTF8Encoding $false))
    $substituted++
}
Write-Host "rename: substituted $substituted files"

# ── 4. paths ───────────────────────────────────────────────────────────────────────────────────
# Deepest first, so renaming a directory never invalidates a path still queued beneath it.
$renamed = 0
$targets = Get-ChildItem -Recurse -Force -Name |
    Where-Object { $_ -cmatch '__[A-Z][A-Z0-9_]*__' -and $_ -notlike '.git*' } |
    Sort-Object { ($_ -split '[\\/]').Count } -Descending
foreach ($path in $targets) {
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $leaf = Split-Path -Leaf $path
    $new = $leaf
    foreach ($p in $placeholders) { $new = $new.Replace($p.Name, $p.Value) }
    if ($new -ceq $leaf) { continue }
    Rename-Item -LiteralPath $path -NewName $new
    $renamed++
}
Write-Host "rename: renamed $renamed paths"

# ── 5. regenerate what substitution cannot preserve ────────────────────────────────────────────
# Substituting the committed generated code is not the same as generating it. protoc derives Go
# symbol names and the per-language namespace options from the proto package, and it collapses the
# placeholder's underscores on the way: a renamed file says file__smokeapp_… where a generated one
# says file_smokeapp_…. Only regeneration produces the tree buf generate will next be checked
# against.
#
# Not fatal. By this point the tree has already been substituted and renamed, and abandoning it
# half-done is worse than a tree whose generated code is a rename behind — that still compiles,
# because the substituted symbols are consistent with each other. It is the next buf generate that
# would show a diff, so say so.
$regenerated = $false
if (Get-Command buf -ErrorAction SilentlyContinue) {
    buf generate 2>&1 | Out-Null
    $regenerated = $LASTEXITCODE -eq 0
}
if ($regenerated) {
    Write-Host 'rename: regenerated the contract'
} else {
    Write-Warning ("rename: could not regenerate the contract — buf and its protoc-gen-go and " +
        "protoc-gen-connect-go plugins have to be on PATH. Run 'buf generate' before committing, " +
        "or the next run of it will show a diff.")
}

# The analyzer sorts imports by namespace, and a new name sorts somewhere else: Kakehashi sat
# between CommunityToolkit and Microsoft, SmokeApp does not. This is the same command CI verifies
# with --verify-no-changes, so the two cannot disagree.
$solution = Get-ChildItem -Path client -Filter *.slnx -File | Select-Object -First 1
$formatted = $false
if ($solution -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    dotnet format $solution.FullName --severity warn 2>&1 | Out-Null
    $formatted = $LASTEXITCODE -eq 0
}
if ($formatted) {
    Write-Host 'rename: reformatted the client'
} else {
    Write-Warning ("rename: could not reformat the client — the .NET SDK has to be installed. " +
        "Run 'dotnet format client/$($solution.Name)' before committing, or the format check " +
        "will fail on import ordering.")
}

# ── 6. clean ───────────────────────────────────────────────────────────────────────────────────
# XamlCompiler caches the old namespaces under obj/, and a stale cache fails the next build with an
# error naming a type nobody wrote.
Get-ChildItem -Recurse -Force -Directory |
    Where-Object { $_.Name -in @('obj', 'bin') -and $_.FullName -notlike '*\.git\*' } |
    ForEach-Object { Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue }
if (Test-Path '.buf-cache') { Remove-Item -Recurse -Force '.buf-cache' }

# ── 7. self-check ──────────────────────────────────────────────────────────────────────────────
# The paths renamed above are untracked until the next commit, so this walks the working tree
# rather than asking git what it knows about.
#
# The CLI named as a tool is exempt, and is the only thing that is: the generator's markers, the
# tool's own paths in the project, and a command somebody is told to run. A renamed project runs
# `kakehashi add module` the way it runs `docker compose up`, and saying so is not the template
# leaking. Keep this in agreement with toolPattern in tools/cli/internal/scaffold/selfcheck.go.
#
# Line-based, where the CLI's check is positional. A line that both runs the tool and names the
# template gets past this one, and the CLI's is the check that does not.
$pattern = '__[A-Z][A-Z0-9_]*__|Kakehashi|kakehashi|KAKEHASHI|SekiroKenjii|架け橋'
$exempt = 'kakehashi:[a-z0-9-]+:(begin|end)|\.kakehashi(\.json|/)|\bkakehashi (new|add|remove|doctor|version|upgrade)\b'
$leftovers = Get-ChildItem -Recurse -File -Force |
    Where-Object { $_.FullName -notlike '*\.git\*' -and $_.FullName -notlike "*$([IO.Path]::DirectorySeparatorChar)tools$([IO.Path]::DirectorySeparatorChar)rename*" } |
    Where-Object { [Array]::IndexOf([System.IO.File]::ReadAllBytes($_.FullName), [byte]0) -lt 0 } |
    Select-String -Pattern $pattern -CaseSensitive |
    Where-Object { $_.Line -cnotmatch $exempt }

if ($leftovers) {
    Write-Host 'rename: the tree still names the template:' -ForegroundColor Red
    $leftovers | ForEach-Object { Write-Host "  $($_.RelativePath($root)):$($_.LineNumber): $($_.Line.Trim())" }
    exit 1
}
Remove-Item -Recurse -Force 'tools/rename'
Write-Host 'rename: self-check clean'

# ── 8. next ────────────────────────────────────────────────────────────────────────────────────
Write-Host @"

  $AppName is ready.

    docker compose up -d
    curl http://localhost:8080/healthz
    dotnet build client/$AppName.slnx -p:Platform=x64
    dotnet run --project client/src/App/$AppName.App/$AppName.App.csproj -p:Platform=x64

  Commit before you start:  git add -A; git commit -m "chore: scaffold from the template"
"@
