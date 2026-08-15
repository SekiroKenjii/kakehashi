---
name: winui-page-design
description: XAML/UX conventions for pages in this WinUI 3 app — breadcrumb header, card layout, caption labels, pills, paged lists, ContentDialogs, x:Bind/MVVM wiring. Use whenever creating a new Page/View or redesigning an existing one, so every page looks and behaves consistently (AccountPage is the reference implementation).
---

# WinUI page design conventions

The reference implementation is
[AccountPage.xaml](../../../src/Modules/Auth/__APP_NAME__.Modules.Auth.UI/Views/AccountPage.xaml) +
[AccountViewModel.cs](../../../src/Modules/Auth/__APP_NAME__.Modules.Auth.UI/ViewModels/AccountViewModel.cs).
Copy its patterns rather than inventing new ones.

## Page skeleton

```xml
<Page ... behaviors:NavigationViewHeaderBehavior.HeaderMode="Never">
    <Page.Resources>  <!-- the shared styles below -->  </Page.Resources>
    <ScrollViewer>
        <StackPanel MaxWidth="1240" Padding="24" Spacing="16">
            <!-- breadcrumb, InfoBar, content -->
        </StackPanel>
    </ScrollViewer>
</Page>
```

- XAML uses **4-space indent**; C# uses 2 (repo `.editorconfig`, `dotnet format` gate).
- The page class takes its view model via constructor injection, exposes it as a
  `ViewModel` property, and triggers `LoadCommand` from `Loaded`.
- Always use compiled `x:Bind` (`Mode=OneWay` for anything the VM mutates); never `{Binding}`.
- `bool` binds directly to `Visibility`. For inverses, expose a computed property
  (`IsSignedOut => !IsAuthenticated`) and raise it with `[NotifyPropertyChangedFor]`.

## Header: breadcrumb, not a title

Pages show a breadcrumb trail ("__APP_TITLE__ › PageName"), not a lone `TitleTextBlockStyle` title:

```xml
<StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
    <TextBlock Foreground="{ThemeResource TextFillColorTertiaryBrush}" Text="__APP_TITLE__" />
    <FontIcon FontSize="12" Foreground="{ThemeResource TextFillColorTertiaryBrush}" Glyph="&#xE76C;" />
    <TextBlock FontWeight="SemiBold" Text="PageName" />
</StackPanel>
```

## Shared styles (copy into Page.Resources)

| Style | Purpose |
| --- | --- |
| `CardStyle` (Border) | `CardBackgroundFillColorDefaultBrush` bg, `CardStrokeColorDefaultBrush` 1px border, `CornerRadius=8`, `Padding=20` |
| `CaptionLabelStyle` (TextBlock) | Section headers: `FontSize=11`, `CharacterSpacing=50`, tertiary foreground, ALL-CAPS text |
| `FieldLabelStyle` / `FieldValueStyle` | Key–value rows: secondary label column (~170px), semibold trimmed value |
| `PillStyle` (Border) | Status chips: subtle bg, `CornerRadius=10`, `Padding=9,3` |
| `PagerButtonStyle` (Button) | Transparent chevron buttons for list paging |

Content is organized as **cards**: `Border Style=CardStyle` → `StackPanel Spacing=10..14` →
caption label → rows. Two-column layouts use a `Grid` with `*` + fixed (≈400px) columns,
`ColumnSpacing=16`.

## Typography & color

- Identifiers (emails, IPs, codes): `FontFamily="Cascadia Mono,Consolas"`, smaller size,
  `TextFillColorSecondaryBrush`.
- Timestamps/metadata: `FontSize=11`, `TextFillColorTertiaryBrush`. Relative time via the
  `FormatRelative` helper pattern ("now", "3h ago", then `MMM d, yyyy`).
- Destructive affordances: `SystemFillColorCriticalBrush` foreground (caption or button).
- Success/positive: `SystemFillColorSuccessBrush` (+ `...SuccessBackgroundBrush` pill bg).
- Status dot: 7px `Ellipse` next to 12px text inside a pill.
- Icons: Segoe Fluent `FontIcon` glyphs. In data templates, two stacked `FontIcon`s with
  `Visibility="{x:Bind IsX}"` / `IsNotX` switch normal vs. alert coloring (no converters).

## Lists with paging (default 5 per page)

Lists in cards page client-side, 5 rows per page:

- VM keeps the full `List<T>` privately, exposes an `ObservableCollection<T>` page window,
  `Has...Paging` (only true beyond one page), a `"current / total"` page label, and
  `...PrevPage`/`...NextPage` relay commands (see `ShowSessionsPage`).
- Card header is a `Grid`: caption left; right-aligned pager
  (`PagerButtonStyle` chevrons `&#xE76B;`/`&#xE76C;` + label) with
  `Visibility="{x:Bind ViewModel.Has...Paging}"`.
- Item templates: `DataTemplate x:DataType="vm:RowRecord"` over sealed records exposing
  get-only properties (plus computed inverses). Per-row actions use a `Click` handler with
  `Tag="{x:Bind}"` that forwards to the VM command.

## Edits happen in ContentDialogs, not the browser

Declare `ContentDialog`s at the end of the page tree (they inherit `XamlRoot`), open them from
`Click` handlers, and validate without closing:

```csharp
private async void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args) {
  var deferral = args.GetDeferral();
  try {
    args.Cancel = !await ViewModel.SaveAsync();   // false ⇒ DialogError set, dialog stays open
  } finally {
    deferral.Complete();
  }
}
```

The VM exposes `Prepare...` (reset/prefill fields + `DialogError = null`) and a
`Task<bool> SaveAsync` that returns false after setting `DialogError`. The dialog body starts
with an error `InfoBar` bound to `HasDialogError`/`DialogError`.

## Errors & refresh

- Page-level failures surface in one `InfoBar Severity="Error"` under the breadcrumb, bound to
  `HasError`/`ErrorMessage`; gateway `Result` failures set it — never throw for expected errors.
- Pages that show session-dependent data register for `AuthSessionChangedMessage` (CommunityToolkit
  `WeakReferenceMessenger`) in the constructor and re-run `LoadCommand` via
  `DispatcherQueue.TryEnqueue` — this is what refreshes the page after a re-login.

## Checklist for a new page

1. Breadcrumb header, `ScrollViewer` + `StackPanel MaxWidth=1240 Padding=24 Spacing=16`.
2. Copy the shared styles; structure content as caption-labelled cards.
3. VM: `ViewModel` base class, `[ObservableProperty]` partials, `ISender` only, `Result`-based
   error handling into `ErrorMessage`.
4. Lists: 5-per-page client paging as above; rows are sealed records.
5. Edits via ContentDialog with deferral-based validation.
6. Register the page + VM transient in the module's `RegisterServices`.
7. Gates: `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes` all green.
