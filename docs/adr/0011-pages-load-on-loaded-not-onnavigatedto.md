# 0011. Pages load on FrameworkElement.Loaded, not OnNavigatedTo

Date: 2026-08-12
Status: accepted

## Context
Pages take their view model through the constructor, so `NavigationService` resolves them from the
DI container. `Frame.Navigate` cannot do that: the frame's reflection-based journal instantiates
page types itself and requires a parameterless constructor. The service therefore sets
`Frame.Content` directly and keeps its own back stack. With no `Frame.Navigate` call, the
`OnNavigatedTo`/`OnNavigatedFrom` overrides never fire — a page that started its initial load
there would silently show nothing.

## Decision
Every page subscribes to `FrameworkElement.Loaded` in its constructor and starts the view model's
initial load from that handler (typically `ViewModel.LoadCommand.ExecuteAsync`). UsersPage,
RolePermissionsPage, and ActivityPage all follow this pattern; every page in the app loads the
same way. Navigation observers subscribe to the service's own `OnNavigated` observable, not to
frame events.

## Consequences
- Constructor injection works for pages; an unregistered page resolves to null and the navigation
  is refused rather than crashing.
- `OnNavigatedTo` is dead code in this app: a page that overrides it never loads and fails without
  an error. New pages must wire `Loaded` instead.
- Pages resolve fresh per navigation (including `GoBack`), so `Loaded` reruns the load on each
  open. ActivityPage depends on this: it refreshes on open and on demand, with no polling timer.
- `NavigateTo` is the single choke point where detached-module pages are refused; calling
  `Frame.Navigate` directly would bypass both the back stack and that check.
