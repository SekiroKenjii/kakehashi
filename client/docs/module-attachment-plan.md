# Module attach / detach — design plan

> Status: **Phase 1 implemented** (local attach/detach, persisted per Windows user). **Phase 2
> (backend-managed assignments) waits on the `assignments` module in
> [`../../server`](../../server) — see the P4 row in the root [README](../../README.md).**
> Registry/view-model unit tests are tracked in [testing-strategy.md](testing-strategy.md) (the
> Phase 1 tests were deferred there with the rest of the host/UI test gap).

Goal: let a user attach a module from the Home page ("+ Register your module") and detach one
from a feature-module tile (an `x` affordance), locally first, and later driven by the backend
where an admin assigns modules that ordinary users cannot detach, while each account can also
attach its own modules.

## Scope and vocabulary

- **Attach** = make a *compiled-in* module part of the user's composition: nav-rail item(s),
  Home tile, navigable pages. **Detach** = remove it from that composition.
- All modules stay compiled into the app and keep their services registered at startup.
  Attachment is a **runtime composition state**, not assembly loading. True plugin loading
  (external DLLs at runtime) is explicitly out of scope: WinUI XBF/PRI resource resolution for
  foreign assemblies is fragile, and the architecture tests could not see runtime references.
- The Auth module is **required** (the startup sign-in gate depends on it) and is never
  detachable. The Settings page is host chrome, not a module — no `x`.

## Phase 1 — local attach/detach (no backend)

1. **Module metadata.** Add a descriptor to `Kakehashi.UI.Contracts` (as shipped — `Name` stays
   the single identity on `IModule`, so the descriptor does not duplicate it):

   ```csharp
   public sealed record ModuleDescriptor(
       string DisplayName, string Description, bool IsRequired);
   ```

   `IModule` gains `ModuleDescriptor Descriptor { get; }`. Catalog → detachable;
   Auth → `IsRequired = true`. (The Home page's hardcoded tile descriptions move here.)

2. **`IModuleRegistry`** (contract in `UI.Contracts`, implementation in the host, singleton):

   ```csharp
   IReadOnlyList<IModule> All { get; }        // every composed module
   IReadOnlyList<IModule> Attached { get; }   // current composition
   bool IsAttached(string name);
   Result Attach(string name);                // fails: unknown name
   Result Detach(string name);                // fails: unknown, required, (later) admin-locked
   ```

   Changes broadcast a `ModuleSetChangedMessage` via `WeakReferenceMessenger` — the same
   pattern as `AuthSessionChangedMessage`, so the shell and Home page refresh without polling.

3. **Persistence.** `ILocalSettingsService` key `Modules.Detached` storing detached module
   names. Default-attached semantics: a module absent from the list is attached, so a newly
   compiled-in module appears automatically on first run.

4. **Shell reaction.** Extract `ShellPage.OnShellPageLoaded`'s nav-item construction into a
   rebuild method that reads `IModuleRegistry.Attached`; re-run it on `ModuleSetChangedMessage`
   (via `DispatcherQueue.TryEnqueue`). If the currently shown page belongs to a detached
   module, navigate Home and clear the back stack (stale entries would resolve detached pages).

5. **Navigation guard.** `NavigationService.NavigateTo` refuses keys whose page type belongs to
   a detached module (single choke point — protects flyouts, deep links, and back navigation).

6. **Home page UX.**
   - Tiles of detachable attached modules get a top-right `x` (hover-revealed Button). Click →
     confirm `ContentDialog` ("You can re-attach it from *Register your module*") → `Detach`.
   - The ghost "+ Register your module" card becomes clickable when detached modules exist:
     opens a `ContentDialog` listing `All − Attached` with name/description and an Attach
     button per row. When nothing is detachable it stays the current inert hint.
   - Tiles and the getting-started "register" step rebuild on `ModuleSetChangedMessage`.

7. **Tests.** ✅ Done — `Kakehashi.App.Tests` covers attach/detach round-trip, required-module
   rejection, persistence of the detached set, default-attached semantics, unknown-name failure,
   and the change broadcast (T1 in [testing-strategy.md](testing-strategy.md), which also opened the
   path for the wider host/UI test gap). Architecture tests are untouched (no new cross-module
   references).

## Phase 2 — backend-managed assignments

Depends on the server's `assignments` module and the roles the `identity` module puts in the access
token. The endpoints below are served by [`../../server`](../../server), which enforces RBAC
server-side; the client consumes them. Being a monorepo, both sides of this land in one pull
request.

1. **Port + use cases** (Application layer of a new `ModuleManagement` module, or host-level
   application abstractions) — the adapter calls the backend's `GET /modules/assignments` and
   `POST /modules/{name}/attach|detach`:

   ```csharp
   public interface IModuleAssignmentGateway {
     Task<Result<IReadOnlyList<ModuleAssignmentDto>>> GetAssignmentsAsync(CancellationToken ct);
     Task<Result> AttachAsync(string moduleName, CancellationToken ct);
     Task<Result> DetachAsync(string moduleName, CancellationToken ct);
   }
   // ModuleAssignmentDto: ModuleName, Source (AdminAssigned | UserAttached), CanDetach
   ```

   Exposed through mediator use cases (`GetModuleAssignmentsQuery`, `AttachModuleCommand`,
   `DetachModuleCommand`); the concrete adapter calls the BE over the existing
   `IBackendClient`-style transport.

2. **Registry becomes a merge.** Attached = required (compiled-in) ∪ admin-assigned (locked
   for non-admins) ∪ user-attached (detachable). The registry resolves the set per signed-in
   account (keyed by user id) and refreshes on `AuthSessionChangedMessage`.

3. **RBAC.** Roles come from the session claims (`SessionDto.Roles`). Non-admins see a lock
   badge instead of `x` on admin-assigned tiles; `Detach` returns a failure `Result` for them
   regardless of UI state. Admin assignment management itself is server-side (per-account
   module assignment table) and surfaced in a future Admin page.

4. **Offline behavior.** The Phase 1 local settings entry becomes a cache of the last server
   answer: when the BE is unreachable the cached set is used read-only (attach/detach disabled
   with a tooltip), and it reconciles on the next successful sync — server wins on conflict.

5. **Security note.** Client-side attachment is UX composition only. Real authorization stays
   on the BE: a module's APIs validate scopes/roles server-side no matter what the client
   shows or hides.

## Suggested sequencing

| Step | Contents | Size | State |
| --- | --- | --- | --- |
| PR 1 | Descriptor + registry + persistence + shell/Home rebuild | small | ✅ done |
| PR 2 | Attach dialog, detach `x` + confirm, navigation guard | small | ✅ done |
| T1 | Registry unit tests (see [testing-strategy.md](testing-strategy.md)) | small | ✅ done |
| PR 3 | (with BE) gateway port + use cases, merge/sync, RBAC lock states | medium | ⛔ blocked on backend P2 |

> Registry tests moved out of PR 2 (shipped without them) into the testing-strategy backlog, since
> they need the host test project that effort introduces.
