# Testing strategy — closing the unit-test gap

> Status: **done (T1–T4).** The host and UI/presentation layers have unit coverage:
> `__APP_NAME__.App.Tests` (45) and `__APP_NAME__.Modules.Auth.UI.Tests` (20) — across `ModuleRegistry`
> (13), `HomeViewModel` (16), `AppActivityLog` (9), `NavigationService.GetPageKey` (3),
> `SettingsViewModel` (2), `AppOrchestrator` (2), `AccountViewModel` (16) and
> `AccountFlyoutViewModel` (4). The windows-TFM unit-test approach (Option A) is the documented
> default for new modules (CLAUDE.md "Adding a new module" + "View model tests").
> **Client total: 146 tests across 12 suites** (143 run everywhere, plus 3 that need a backend).
>
> The `Catalog` module was removed when the client was ported into __APP_TITLE__, taking its four
> `ProductsViewModel` tests with it. `Notes` replaced it as the reference vertical slice and brought
> 36 of its own: `NoteDraft` (9), the three command handlers (7), the module through the real
> mediator (6), and `NotesViewModel` (14).
>
> `LiveNotesGatewayTests` (3) is the exception to everything else here: it drives the real
> `GrpcNotesGateway` over real gRPC into real SQL Server, and is skipped unless
> `KAKEHASHI_TEST_BACKEND` names a running server. It is the only test that would notice a broken
> migration or a status code the client maps wrongly; the price is that it cannot run on a laptop
> with nothing started, and a suite that fails when the stack is down trains everyone to ignore it.
>
> Remaining as **integration-level** (need a UI-thread `Frame`/XAML, out of scope for unit tests):
> the `NavigationService` detached-module guard + `GoBack` skipping, and `AppOrchestrator`'s
> awake-service loop (touches the app's static service provider).
>
> Prompted by the module-registry tests being deferred, which surfaced a broader gap: the **host and
> UI/presentation layers have no unit tests**. Domain, Application, and host Infrastructure are
> reasonably covered.

## Where coverage actually stands

66 tests across 9 suites today. Mapped to layers:

| Layer / project | Tests | State |
| --- | --- | --- |
| `Modules.*.Domain` | 22 | ✅ covered |
| `Modules.*.Application` (handlers) | 15 | ✅ covered |
| `Modules.*.IntegrationTests` | 8 | ✅ covered |
| `__APP_NAME__.Mediator` | 4 | ✅ covered |
| `__APP_NAME__.App.Infrastructure` (backend clients) | 10 | ✅ covered |
| `__APP_NAME__.ArchitectureTests` | 7 | ✅ covered |
| **`__APP_NAME__.App` (host)** — `ModuleRegistry`, `AppActivityLog`, `NavigationService`, `ThemeService`, `LocalSettingsService`, orchestrators, `StateManager`, view models (`Home`, `Shell`, `Settings`, `Splash`) | **0** | ❌ **none** |
| **`Modules.*.UI`** — `AccountViewModel`, `ProductsViewModel`, `AccountFlyoutViewModel`, gateways, `OidcInteractiveAuthenticator` | **0** | ❌ **none** |
| `__APP_NAME__.UI.Contracts` — `ViewModel` base, the new `ModuleRegistry` contracts | 0 | ❌ none |

So the untested surface is the **presentation + host-composition logic** — exactly where the recent
work landed (module registry, activity log, home view model). That logic is plain C# (ISender,
injected services, `Result`), not XAML, so it is unit-testable; the only blocker is that no test
project targets these assemblies.

## The one real obstacle, and how to clear it

The host (`__APP_NAME__.App`) and module `.UI` projects are `net10.0-windows` WinUI assemblies
(`UseWinUI=true`); the existing test projects are plain `net10.0`. A test project must match the
windows TFM to reference them. Two ways:

- **Option A — windows-TFM test projects (recommended first).** Add `__APP_NAME__.App.Tests` and
  `__APP_NAME__.Modules.<M>.UI.Tests` targeting `net10.0-windows10.0.19041.0`, referencing the
  host/UI projects directly. Test view models and services as plain objects (construct with
  NSubstitute fakes; never instantiate XAML controls). Lowest churn — no production code moves.
  Risk: referencing a `WinExe` host + WinUI-generated code from a test assembly can be finicky;
  validate with one smoke test (`ModuleRegistry`) before fanning out.
- **Option B — extract testable host services into `__APP_NAME__.App.Core`.** Move the non-XAML host
  services (`ModuleRegistry`, `AppActivityLog`, `NavigationService`, orchestrators, `StateManager`)
  into a windows-TFM **library** that a plain test project references, leaving `__APP_NAME__.App` as
  thin composition + XAML. Robust and clean, but a real refactor touching DI wiring.

**Recommendation:** start with Option A and prove it on `ModuleRegistry`. If WinUI host-referencing
fights us, fall back to Option B for the host services (the UI view models still need windows-TFM
test projects regardless). Decide before writing the second test class.

## Patterns (unchanged from CLAUDE.md)

Same conventions as the Domain/Application tests: one `sealed` test class per SUT, substitutes as
fields, a `CreateX()` factory, `Assert.*` (no Fluent Assertions), `Received(n)`/`DidNotReceive()`.
For view models: a fake `ISender` returns canned `Result`s; assert observable properties and command
effects. Avoid `DispatcherQueue`/XAML — if a class needs the UI thread, that dependency is the smell,
not the test.

## Prioritized backlog (high logic density first)

1. **`ModuleRegistry`** — attach/detach round-trip, required-module rejection, unknown-name failure,
   persistence of the detached set, default-attached semantics, `ModuleSetChangedMessage` broadcast.
   *(This is the deferred set; it lands first and also validates Option A.)*
2. **`HomeViewModel`** — greeting by time-of-day, getting-started step completion + progress, backend
   "Not configured" vs probe states, activity paging (5/page), tile build from attached modules,
   attach/detach commands and the detach-confirm staging.
3. **`AppActivityLog`** — sign-in/out transition recording, app-update detection, theme-change entry,
   newest-first cap at 50, persistence round-trip.
4. **`AccountViewModel`** (Auth.UI) — sessions/activity client paging, sign-out/revoke command flows,
   dialog validation (`SaveProfileAsync`/`ChangePasswordAsync` Result handling).
5. **`NavigationService`** — page-key derivation (done, unit). The detached-module navigation guard
   and `GoBack` back-stack skipping drive a XAML `Frame` and resolve `Page` instances, so they are
   **integration-level** (need a UI-thread Frame), not headless unit tests — deferred to a UI test.
6. **`SettingsViewModel` / `AccountFlyoutViewModel`** — theme index ↔ `ElementTheme` mapping, relative
   time formatting.
7. **Orchestrators** (`AppOrchestrator` ordering, individual startup steps) — as logic allows.

## Definition of done (per CLAUDE.md gate)

- New test projects added to `__APP_NAME__.slnx`; `dotnet test` green including them.
- Architecture tests still green (test projects don't perturb layering).
- `dotnet format --verify-no-changes` clean.
- CLAUDE.md "Adding a new module" checklist updated to include the `.UI.Tests` project so new modules
  get presentation tests by default, and the testing-conventions section gains a "view model tests"
  note.

## Sequencing

| PR | Contents | Size | State |
| --- | --- | --- | --- |
| T1 | `__APP_NAME__.App.Tests` (Option A) + `ModuleRegistry` tests — proves the approach | small | ✅ done |
| T2 | `HomeViewModel` + `AppActivityLog` tests | small | ✅ done |
| T3 | `__APP_NAME__.Modules.Auth.UI.Tests` + `AccountViewModel`; `NavigationService.GetPageKey` | medium | ✅ done |
| T4 | `SettingsViewModel`, `AccountFlyoutViewModel`, `AppOrchestrator`; CLAUDE.md checklist + view-model-tests note. (Originally also `__APP_NAME__.Modules.Catalog.UI.Tests` + `ProductsViewModel`, removed with the Catalog module.) | small | ✅ done |
