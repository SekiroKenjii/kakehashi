# Architecture

This document describes how the boilerplate is structured and the rules that keep it consistent as
the team and the codebase grow.

## Modular monolith

The application is a **single deployable** (the WinUI host) composed of **feature modules**. Each
module owns a slice of the product end-to-end and is internally layered. Modules are isolated: they
never reference one another's projects. When they must collaborate, they do so through events on the
mediator, not through direct calls.

This gives most of the decoupling benefits of microservices (clear ownership, independent evolution,
testability) without the operational cost of distributed systems.

## The three layers (per module)

```markdown
        ┌────────────────────────────────────────────────┐
        │  UI (host)   — WinUI pages, view models,       │   depends on
        │                infrastructure adapters,        │  ─────────────►  Application
        │                IModule registration            │
        ├────────────────────────────────────────────────┤
        │  Application — use cases (commands/queries +   │   depends on
        │                handlers), ports, DTOs          │  ─────────────►  Domain
        ├────────────────────────────────────────────────┤
        │  Domain      — entities, value objects,        │   depends on
        │                domain events, domain services  │  ─────────────►  (SharedKernel only)
        └────────────────────────────────────────────────┘
```

- **Domain** — the heart. Entities/aggregates (`Product`), value objects (`Money`), domain events
  (`ProductCreatedDomainEvent`), and domain services (`IPricingPolicy`). Pure C#, no framework
  dependencies beyond `__APP_NAME__.SharedKernel`. Invariants live here; expected rule violations are
  returned as `Result`, not thrown.
- **Application** — orchestrates use cases. Each use case is an `IRequest` with a single handler.
  Defines **ports** (e.g. `IProductRepository`) that the host implements. Returns DTOs, never domain
  objects, to the UI.
- **UI (host)** — WinUI pages and `CommunityToolkit.Mvvm` view models. The view model talks to the
  domain **only** through the mediator (`ISender`). This layer also supplies the concrete
  infrastructure adapters (e.g. `InMemoryProductRepository`) and registers the module via `IModule`.

> **Where does infrastructure go?** The brief specifies exactly three layers, so concrete adapters
> (repositories, gateways) live in the UI/host layer and are wired in `IModule.RegisterServices`.
> Because the *port* lives in Application, swapping the adapter (e.g. to EF Core) touches only the
> host. If a module's persistence grows large, promoting infrastructure to its own project is a
> natural, non-breaking next step.

## Shared building blocks

| Project | Responsibility |
| --- | --- |
| `__APP_NAME__.SharedKernel` | Domain primitives: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `IDomainEvent`, `Result`/`Error`. Depends on nothing. |
| `__APP_NAME__.Application.Abstractions` | Mediator contracts (`IRequest`, `IRequestHandler`, `IPipelineBehavior`, `INotification`, `IDomainEventHandler`, `IMediator`) and `IClock`. |
| `__APP_NAME__.Mediator` | The in-process mediator implementation, `AddMediator(...)` registration, and the `LoggingBehavior` pipeline step. |
| `__APP_NAME__.UI.Contracts` | The `IModule` / `NavigationItem` contract between modules and the host. No WinUI dependency. |

## The mediator

A small, dependency-free, MediatR-style dispatcher (MediatR itself is no longer free):

- **Commands / queries** — `ISender.Send(IRequest<TResponse>)` resolves the single
  `IRequestHandler<,>` and runs it through the `IPipelineBehavior<,>` chain (logging, and any
  validation/transaction behaviors you add).
- **Domain events** — aggregates raise `IDomainEvent`s; a handler collects them after persistence and
  `IDomainEventDispatcher` delivers each to its `IDomainEventHandler<>`s. This is the in-module
  reaction mechanism.
- **Integration events** — `IPublisher.Publish(INotification)` fans out to every
  `INotificationHandler<>`. This is the **cross-module** mechanism: a module publishes a notification
  and other modules subscribe, with no project reference between them.

Handlers are discovered by assembly scanning in `AddMediator(params Assembly[])`.

## Dependency rules (enforced)

`__APP_NAME__.ArchitectureTests` fails the build if any of these are violated:

1. **Domain** references nothing but `SharedKernel` — not Application, UI, the host, or the mediator.
2. **Application** does not reference the **UI** or the **host**.
3. **SharedKernel** references no other `__APP_NAME__.*` project.
4. A module's layers never reference **another feature module**.

These are reflection checks over referenced assembly names, so they run fast and without the WinUI
layers loaded.

## Composition & navigation

`__APP_NAME__.App` is the composition root:

1. `ModuleCatalog` lists the `IModule`s in the app (the one place modules are enumerated).
2. `App.OnLaunched` builds the DI container, letting each module register its own services.
3. `MainWindow` builds its `NavigationView` from the modules' `NavigationItem`s.
4. `NavigationService` resolves the selected page **from the container** (so pages get constructor
   injection) and shows it in the shell frame.

## Page skeleton

Every page opens with the shared `PageHeader` control (`__APP_NAME__.UI.Common.Controls`): a
breadcrumb on the left, the page's commands on the right. The commands are the control's XAML
content property, so they need no property element:

```xml
<controls:PageHeader Section="Administration" Title="Users">
    <StackPanel Orientation="Horizontal" Spacing="2">
        <Button Style="{StaticResource AccentToolbarButtonStyle}" …/>
        <Border Style="{StaticResource CommandBarDividerStyle}"/>
        <Button Style="{StaticResource ToolbarButtonStyle}" …/>
    </StackPanel>
</controls:PageHeader>
```

`Root` defaults to the product name so every trail starts at the same place; set `Section` for the
muted middle crumb and `Title` for the page's own name. An empty crumb — and an empty command set —
collapses instead of drawing empty chrome. The control lives in `__APP_NAME__.UI.Common` because
feature modules have pages too, and a control in the host is one they cannot reference.

### A page subscribes on `Loaded` and drops the subscription on `Unloaded`

Never for the life of the object. Pages are transient: navigating away releases the page's WinRT
peer while `WeakReferenceMessenger` still holds the managed object, so the next broadcast reads
`DispatcherQueue` off a disposed peer — an `ObjectDisposedException` that takes the process down.

`UnregisterAll` runs first, because `Register` throws on a duplicate and `Loaded` fires more than
once for the same instance.

### Static helpers on the page, not converters

`x:Bind` calls functions, but it cannot format a string, negate a bool, or pick a brush from one.
Pages carry small `public static` helpers for that instead of `IValueConverter` implementations,
because a function is compile-checked against its arguments and a converter is not — a converter
with the wrong input type fails silently at runtime, on a binding, where nothing points at the
cause.

Pages also start their first load from `FrameworkElement.Loaded`, never `OnNavigatedTo`:
[docs/adr/0011-pages-load-on-loaded-not-onnavigatedto.md](../../docs/adr/0011-pages-load-on-loaded-not-onnavigatedto.md).

## Conventions that aren't tool-enforced

- **Class member order** follows the Google guide: nested types, then static/const/readonly fields,
  then fields & properties, then constructors, then methods; public before non-public within a group.
  (`dotnet format` does not reorder members, so this is a review convention.)
- **Use `Result`** for expected, domain-level failures; reserve exceptions for programmer errors and
  truly exceptional conditions.
- **DTOs cross the Application boundary**, never domain entities.
