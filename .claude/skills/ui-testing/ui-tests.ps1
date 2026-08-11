# Kakehashi pre-release UI regression, driven through UI Automation.
#
# Run it against a running build:
#   dotnet build client\src\App\Kakehashi.App\Kakehashi.App.csproj -c Release -p:Platform=x64
#   $exe = "client\src\App\Kakehashi.App\bin\x64\Release\net10.0-windows10.0.19041.0\Kakehashi.App.exe"
#   $p = Start-Process $exe -PassThru
#   .\.claude\skills\ui-testing\ui-tests.ps1 -AppPid $p.Id
#
# The server must be up (docker compose ps). SKILL.md beside this file records the traps.
#
# Slugs are regenerated whenever the visual tree is rebuilt, so nothing here hard-codes one:
# every selector is resolved by name + control type at the moment it is used.
param(
    [Parameter(Mandatory)][int]$AppPid,
    # Outside the repo on purpose: screenshots and results are evidence of one run, not source.
    [string]$ShotDir = (Join-Path $env:TEMP 'kakehashi-ui')
)

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()
New-Item -ItemType Directory -Force -Path $ShotDir | Out-Null

$hwnd = ((winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json) |
    Where-Object { $_.title -ne 'PopupHost' } | Select-Object -First 1).hwnd
if (-not $hwnd) { throw "no main window for PID $AppPid" }
Write-Host "main window: $hwnd"

# ─────────────────────────────────────────────────────────────── helpers

# Only the -i form returns a flat element array in JSON; the plain form answers with the window
# alone, which is why an earlier version of this script found zero of everything.
function Elements {
    param([switch]$Interactive)
    $j = winapp ui inspect -w $hwnd -i --json 2>$null | ConvertFrom-Json
    if ($j.windows) { return $j.windows[0].elements }
    return @()
}

# Every element on the page, including the Text runs that inspect -i hides.
function AllOf {
    (winapp ui inspect -w $hwnd -d 24 2>$null) -split "`r?`n" | ForEach-Object {
        if ($_ -match '^\s*(\S+)\s+(\w+)\s+"([^"]*)"') {
            [pscustomobject]@{ selector = $Matches[1]; type = $Matches[2]; name = $Matches[3] }
        } elseif ($_ -match '^\s*(\S+)\s+(\w+)\s') {
            [pscustomobject]@{ selector = $Matches[1]; type = $Matches[2]; name = '' }
        }
    }
}

function Find1 {
    param([string]$Name, [string]$Type, [switch]$Enabled, [int]$Index = 0)
    $hits = @((winapp ui search $Name -w $hwnd --json 2>$null | ConvertFrom-Json).matches |
        Where-Object { $_.name -eq $Name -and (-not $Type -or $_.type -eq $Type) -and
                       (-not $Enabled -or $_.isEnabled) } | Sort-Object y)
    if ($hits.Count -le $Index) { return $null }
    $hits[$Index].selector
}

function Label1 {
    param([string]$Text)
    (AllOf | Where-Object { $_.name -eq $Text -and $_.selector -like 'lbl-*' } |
        Select-Object -First 1).selector
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++; $script:results += @{ name = $Name; status = 'PASS' }
            Write-Host "  PASS $Name"
        } else {
            $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$output" }
            Write-Host "  FAIL $Name -- $output" -ForegroundColor Red
        }
    } catch {
        $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
        Write-Host "  FAIL $Name -- $_" -ForegroundColor Red
    }
}

# Assert on something we computed ourselves rather than on an exit code.
function Assert-That {
    param([string]$Name, [bool]$Condition, $Detail = '')
    if ($Condition) {
        $script:pass++; $script:results += @{ name = $Name; status = 'PASS' }
        Write-Host "  PASS $Name"
    } else {
        $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$Detail" }
        Write-Host "  FAIL $Name -- $Detail" -ForegroundColor Red
    }
}

function Choose {
    param([string]$Combo, [string]$Item)
    $hit = @()
    foreach ($attempt in 1, 2) {
        winapp ui invoke $Combo -w $hwnd -q 2>$null | Out-Null
        Start-Sleep 1
        $hit = @((winapp ui search $Item -a $AppPid --json 2>$null | ConvertFrom-Json).matches |
            Where-Object { $_.name -eq $Item -and $_.type -eq 'ListItem' } | Select-Object -First 1)
        if ($hit) { break }
        # The dropdown did not open, or opened and closed again. Settle and try once more.
        winapp ui send-keys 'escape' -w $hwnd --via send-input -q 2>$null | Out-Null
        Start-Sleep 1
    }
    if (-not $hit) {
        winapp ui send-keys 'escape' -w $hwnd --via send-input -q 2>$null | Out-Null
        $global:LASTEXITCODE = 1
        return "no item named $Item in the open dropdown"
    }
    winapp ui invoke $hit[0].selector -a $AppPid -q 2>$null
}

function Shot([string]$name) { winapp ui screenshot -w $hwnd -o "$ShotDir\$name.png" -q 2>$null | Out-Null }

# Waits for the page to settle rather than sleeping a fixed three seconds. A page that fetches from
# the server can take longer than that on a cold start, and the whole page then reads as empty -
# which produced a run reporting no accounts and no roles on screens that were in fact fine.
function GoTo([string]$page) {
    $s = Find1 -Name $page -Type 'ListItem'
    if (-not $s) { return $false }
    winapp ui invoke $s -w $hwnd -q 2>$null | Out-Null

    $last = -1
    foreach ($attempt in 1..10) {
        Start-Sleep 1
        $count = @(Elements -Interactive).Count
        # Two readings the same means the page has stopped adding to itself.
        if ($count -eq $last -and $count -gt 0) { return $true }
        $last = $count
    }
    $true
}

function PaneItems {
    @(Elements -Interactive | Where-Object { $_.className -like '*NavigationViewItem*' } |
        Select-Object -ExpandProperty name)
}

function Section([string]$t) { Write-Host "`n== $t" -ForegroundColor Cyan }

# ─────────────────────────────────────────────────────────────── shell

Section 'Shell and navigation'

$items = PaneItems
Assert-That 'the pane was built from the server layout' ($items.Count -ge 7) ($items -join ', ')
foreach ($expected in @('Home', 'Notes', 'Activity', 'Users', 'Role permissions', 'Navigation', 'Settings')) {
    Assert-That "the pane offers $expected" ($items -contains $expected) ($items -join ', ')
}
Assert-That 'the account footer item has an accessible name' (-not ($items -contains 'NavigationViewItem')) `
    'a NavigationViewItem with no name is announced as its class name'

Test-UI 'the pane collapses' { winapp ui invoke 'PART_PaneToggleButton' -w $hwnd }
Start-Sleep 1
Test-UI 'the pane expands again' { winapp ui invoke 'PART_PaneToggleButton' -w $hwnd }
Start-Sleep 1

foreach ($page in @('Home', 'Notes', 'Activity', 'Users', 'Role permissions', 'Navigation', 'Settings')) {
    Assert-That "$page opens" (GoTo $page) 'nav item not found or not invokable'
}
Shot '30-settings'

# ─────────────────────────────────────────────────────────────── settings and the theme write path

Section 'Settings, and the client write path for ThemeChanged'

$theme = Find1 -Name 'Theme' -Type 'ComboBox'
Assert-That 'the theme picker is reachable and named' ($null -ne $theme) 'no ComboBox named Theme'
if ($theme) {
    $before = (winapp ui get-value $theme -w $hwnd --json 2>$null | ConvertFrom-Json).text
    Test-UI 'the theme can be set to Dark' { Choose $theme 'Dark' }
    Start-Sleep 2
    Shot '31-theme-dark'
    Test-UI 'the theme can be put back' { Choose $theme $before }
    Start-Sleep 2
    Assert-That 'the theme returned to where it started' `
        ((winapp ui get-value $theme -w $hwnd --json 2>$null | ConvertFrom-Json).text -eq $before) $before
}

# ─────────────────────────────────────────────────────────────── activity

Section 'Activity'

Assert-That 'Activity opens' (GoTo 'Activity') ''
Shot '32-activity'

$chips = @('All', 'Sign-ins', 'Security', 'System')
foreach ($chip in $chips) {
    $s = @(Elements -Interactive | Where-Object {
        $_.type -eq 'Button' -and $_.name -like "$chip,*"
    } | Select-Object -First 1).selector
    Assert-That "the $chip chip exists" ($null -ne $s) 'chip not found in the tree'
    if ($s) {
        Test-UI "the $chip chip filters" { winapp ui invoke $s -w $hwnd }
        Start-Sleep 2
    }
}
Shot '33-activity-system-chip'

$all = @(Elements -Interactive | Where-Object { $_.type -eq 'Button' -and $_.name -like 'All,*' } |
    Select-Object -First 1).selector
Assert-That 'the feed can be put back to every category' ($null -ne $all) 'no All chip'
if ($all) { winapp ui invoke $all -w $hwnd -q 2>$null | Out-Null; Start-Sleep 3 }

$rangeBox = @(Elements -Interactive | Where-Object { $_.type -eq 'ComboBox' })
Assert-That 'the date range picker is on the page' ($rangeBox.Count -ge 1) 'no ComboBox found'
if ($rangeBox.Count -ge 1) {
    Test-UI 'the range can be widened to 90 days' { Choose $rangeBox[0].selector 'Last 90 days' }
    Start-Sleep 3
    Shot '34-activity-90-days'
}

$search = @(Elements -Interactive | Where-Object { $_.type -in @('Edit', 'Group') -and $_.name -match 'Search|search' })
$searchSel = if ($search.Count) { $search[0].selector } else { $null }
Assert-That 'the search box is reachable' ($null -ne $searchSel) 'no search input found'
if ($searchSel) {
    Test-UI 'a search term can be typed' {
        winapp ui send-keys 'Windows' --target $searchSel -w $hwnd --via send-input
    }
    Start-Sleep 1
    Test-UI 'submitting the search runs it' { winapp ui send-keys 'enter' -w $hwnd --via send-input }
    Start-Sleep 3
    Shot '35-activity-search'
}

# The search above still narrows the feed, which can leave it with no second page at all.
if ($searchSel) {
    winapp ui send-keys 'ctrl+a' --target $searchSel -w $hwnd --via send-input -q 2>$null | Out-Null
    winapp ui send-keys 'delete' -w $hwnd --via send-input -q 2>$null | Out-Null
    winapp ui send-keys 'enter' -w $hwnd --via send-input -q 2>$null | Out-Null
    Start-Sleep 3
}

$loadMore = Find1 -Name 'Load more'
Assert-That 'Load more is a named, reachable control' ($null -ne $loadMore) `
    'not found - either no next page, or the button has no accessible name'
if ($loadMore) {
    function ShownCount {
        # Read the label rather than the inspect dump: the text form wraps long lines, so the
        # footer sentence is split across two of them and never matches whole.
        $sel = @(AllOf | Where-Object { $_.selector -like 'lbl-showing*' } | Select-Object -First 1).selector
        if (-not $sel) { return -1 }
        $text = (winapp ui get-value $sel -w $hwnd --json 2>$null | ConvertFrom-Json).text
        if ($text -match 'Showing (\d+) of') { return [int]$Matches[1] }
        -1
    }
    $shownBefore = ShownCount
    Test-UI 'Load more appends' { winapp ui invoke $loadMore -w $hwnd }
    Start-Sleep 3
    $shownAfter = ShownCount
    Assert-That 'the page grew rather than being replaced' ($shownAfter -gt $shownBefore) `
        "the footer said $shownBefore, then $shownAfter"
    Shot '36-activity-load-more'
}

$refresh = Find1 -Name 'Refresh' -Type 'Button'
if ($refresh) { Test-UI 'Activity refreshes' { winapp ui invoke $refresh -w $hwnd }; Start-Sleep 3 }

# ─────────────────────────────────────────────────────────────── navigation

Section 'Navigation'

Assert-That 'Navigation opens' (GoTo 'Navigation') ''
Shot '40-navigation'

$headings = @(Elements | Where-Object { $_.name -eq 'Heading name' -and -not $_.isOffscreen })
Assert-That 'the stored headings are drawn' ($headings.Count -ge 2) "$($headings.Count) heading rows"

# Both checks matter, and the second cannot stand alone: with no eyes at all it passes trivially,
# which is how a regression that took every eye out of the tree first read as green.
$eyes = @(Elements -Interactive | Where-Object { $_.name -match '(offered in|hidden from) the pane' })
Assert-That 'every screen row offers a visibility control' ($eyes.Count -ge 5) `
    "$($eyes.Count) eyes - a row wrapped in a Button hides the controls inside it"
$named = @($eyes | Where-Object { $_.name -match '^\S.*: (offered in|hidden from) the pane$' })
Assert-That 'every eye says which screen it acts on and what state it is in' `
    (($eyes.Count -ge 5) -and ($named.Count -eq $eyes.Count)) `
    "$($named.Count) of $($eyes.Count) eyes are named per row"

# inspect -i lists only invokable types, and a focusable Border is exposed as a Group, so the rows
# have to be read from the full tree. Reading them from the interactive one makes a row that a
# keyboard user can reach and press Enter on report as missing.
$rows = @(AllOf | Where-Object { $_.selector -like 'grp-*' -and
    $_.name -in @('Notes', 'Activity', 'Users', 'Role permissions', 'Navigation') })
Assert-That 'every screen row is a named, reachable element' ($rows.Count -ge 5) `
    "$($rows.Count) rows - an unnamed Border is invisible to UIA and to the keyboard"

$notesLbl = Label1 'Notes'
if ($notesLbl) {
    Test-UI 'selecting a screen opens the editor' { winapp ui click $notesLbl -w $hwnd }
    Start-Sleep 2
    $reset = Find1 -Name 'Reset to code defaults'
    Assert-That 'the editor offers a reset to the code defaults' ($null -ne $reset) 'button not found'
    Shot '41-nav-editor'

    $up = Find1 -Name 'Move up' -Type 'Button'
    $down = Find1 -Name 'Move down' -Type 'Button'
    Assert-That 'the editor has both chevrons for a screen' (($null -ne $up) -and ($null -ne $down)) `
        "up=$up down=$down"
}

$hUp = Find1 -Name 'Move heading up' -Type 'Button'
$hDown = Find1 -Name 'Move heading down' -Type 'Button'
Assert-That 'a heading can be moved up' ($null -ne $hUp) 'no move-heading-up button'
Assert-That 'a heading can be moved down' ($null -ne $hDown) `
    'only "move heading up" exists, so the last heading can never be pushed down'

# The picker lives in the preview panel now, so it is not in the tree until the panel is open.
$previewToggle = Find1 -Name 'Pane preview' -Type 'Button'
Assert-That 'the preview can be opened from the command bar' ($null -ne $previewToggle) 'no toggle'
if ($previewToggle) {
    winapp ui invoke $previewToggle -w $hwnd -q 2>$null | Out-Null
    Start-Sleep 2
    Shot '45-preview-open'
}

$preview = Find1 -Name 'Preview the pane as' -Type 'ComboBox'
Assert-That 'the preview-as picker is reachable and named' ($null -ne $preview) 'not found'
if ($preview) {
    Test-UI 'the pane can be previewed as another role' { Choose $preview 'Viewer' }
    Start-Sleep 3
    Shot '42-nav-preview-viewer'
    Test-UI 'the preview returns to your own pane' { Choose $preview 'Yourself' }
    Start-Sleep 2
}

if ($previewToggle) {
    winapp ui invoke $previewToggle -w $hwnd -q 2>$null | Out-Null
    Start-Sleep 1
}

$newHeading = Find1 -Name 'New heading' -Type 'Button'
Assert-That 'a heading can be added' ($null -ne $newHeading) 'not found'
if ($newHeading) {
    Test-UI 'adding a heading stages a change' { winapp ui invoke $newHeading -w $hwnd }
    Start-Sleep 2
    $bar = @(AllOf | Where-Object { $_.name -match 'unsaved change' })
    Assert-That 'the pending bar appears for a staged heading' ($bar.Count -ge 1) 'no pending bar'
    Shot '43-nav-pending'

    $diff = Find1 -Name 'View diff' -Type 'Button'
    Assert-That 'the staged work can be inspected before applying' ($null -ne $diff) 'no View diff button'
    if ($diff) {
        Test-UI 'the diff dialog opens' { winapp ui invoke $diff -w $hwnd }
        Start-Sleep 2
        Shot '44-nav-diff'
        winapp ui send-keys 'escape' -w $hwnd --via send-input -q 2>$null | Out-Null
        Start-Sleep 1
    }

    $discard = Find1 -Name 'Discard' -Type 'Button'
    if ($discard) {
        Test-UI 'Discard drops the staged work' { winapp ui invoke $discard -w $hwnd }
        Start-Sleep 2
        $bar2 = @(AllOf | Where-Object { $_.name -match 'unsaved change' })
        Assert-That 'nothing is left staged after Discard' ($bar2.Count -eq 0) 'pending bar still there'
    }
}

# ─────────────────────────────────────────────────────────────── notes

Section 'Notes'

Assert-That 'Notes opens' (GoTo 'Notes') ''
Shot '50-notes'
$notesButtons = @(Elements -Interactive | Where-Object { $_.type -eq 'Button' -and -not $_.name })
Assert-That 'every button on Notes has an accessible name' ($notesButtons.Count -eq 0) `
    "$($notesButtons.Count) unnamed: $(($notesButtons | ForEach-Object { $_.selector }) -join ', ')"

# ─────────────────────────────────────────────────────────────── users

Section 'Users'

Assert-That 'Users opens' (GoTo 'Users') ''
Shot '60-users'
$accounts = @(Elements -Interactive | Where-Object { $_.type -eq 'ListItem' -and $_.className -notlike '*NavigationViewItem*' })
Assert-That 'the account directory came from the server' ($accounts.Count -ge 1) "$($accounts.Count) rows"
if ($accounts.Count) {
    Test-UI 'an account opens its detail panel' { winapp ui invoke $accounts[0].selector -w $hwnd }
    Start-Sleep 3
    Shot '61-users-detail'
}
foreach ($b in @('Add user', 'Export', 'Refresh')) {
    Assert-That "Users offers $b" ($null -ne (Find1 -Name $b -Type 'Button')) 'not found'
}

# ─────────────────────────────────────────────────────────────── role permissions

Section 'Role permissions'

Assert-That 'Role permissions opens' (GoTo 'Role permissions') ''
Shot '70-roles'
$roles = @(Elements -Interactive | Where-Object { $_.type -eq 'ListItem' -and $_.name -in @('Admin', 'Developer', 'Guest', 'Operations', 'Viewer') })
Assert-That 'the roles came from the server' ($roles.Count -ge 3) "$($roles.Count) roles"
if ($roles.Count) {
    Test-UI 'a role opens its matrix' { winapp ui invoke ($roles | Where-Object { $_.name -eq 'Viewer' } | Select-Object -First 1).selector -w $hwnd }
    Start-Sleep 3
    Shot '71-roles-viewer'
}
$allOn = Find1 -Name 'All on' -Type 'Button'
if ($allOn) {
    Test-UI 'a permission group can be turned all on' { winapp ui invoke $allOn -w $hwnd }
    Start-Sleep 2
    $bar = @(AllOf | Where-Object { $_.name -match 'unsaved|change' })
    Assert-That 'the grant change is staged, not written' ($bar.Count -ge 1) 'no pending indicator'
    Shot '72-roles-staged'
    $discard = Find1 -Name 'Discard' -Type 'Button'
    if (-not $discard) { $discard = Find1 -Name 'Discard changes' -Type 'Button' }
    if ($discard) {
        Test-UI 'the grant change can be discarded' { winapp ui invoke $discard -w $hwnd }
        Start-Sleep 2
    } else {
        Assert-That 'Role permissions offers a Discard' $false 'no discard button found - staged grants cannot be abandoned'
    }
}

# ─────────────────────────────────────────────────────────────── account flyout

Section 'Account'

$footer = @(Elements -Interactive | Where-Object { $_.className -like '*NavigationViewItem*' -and $_.name -eq 'NavigationViewItem' })
if ($footer.Count) {
    Test-UI 'the account footer item opens' { winapp ui invoke $footer[0].selector -w $hwnd }
    Start-Sleep 2
    Shot '80-account-flyout'
}

# ─────────────────────────────────────────────────────────────── accessibility sweep

Section 'Accessibility sweep across every page'

$unnamed = @()
foreach ($page in @('Home', 'Notes', 'Activity', 'Users', 'Role permissions', 'Navigation', 'Settings')) {
    if (-not (GoTo $page)) { continue }
    $bad = @(Elements -Interactive | Where-Object {
        $_.type -in @('Button', 'Edit', 'ComboBox', 'CheckBox') -and -not $_.name -and -not $_.automationId
    })
    if ($bad.Count) { $unnamed += [pscustomobject]@{ page = $page; count = $bad.Count } }
    Write-Host ("  {0,-18} {1} unnamed interactive control(s)" -f $page, $bad.Count)
}
Assert-That 'no page has an unnamed interactive control' ($unnamed.Count -eq 0) `
    (($unnamed | ForEach-Object { "$($_.page): $($_.count)" }) -join '; ')

# ─────────────────────────────────────────────────────────────── results

Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) -- $($_.detail)" -ForegroundColor Red
}
$results | ConvertTo-Json -Depth 4 | Out-File "$ShotDir\test-results.json" -Encoding utf8
if ($fail -gt 0) { exit 1 } else { exit 0 }
