# Kakehashi — WinUI 3 client

The Windows half of [Kakehashi](../README.md), built with **WinUI 3 / Windows App SDK** on
**.NET 10**. It is organized as a **modular monolith**: one deployable app composed of independent
feature modules, each split into three layers — **UI (host) → Application (use cases) → Domain**.

It talks to the Go server through the schema in [`../proto`](../proto), from which `Grpc.Tools`
generates the typed client at build time. There is no copy of the schema here to drift.

Every choice here optimizes for many developers working in one codebase over a long time:
consistency is enforced by tooling, not by review etiquette.

## Highlights

- **WinUI 3** desktop host (`Microsoft.WindowsAppSDK` 2.1.x), configurable **unpackaged or packaged (MSIX)**.
- **Modular monolith** with strict per-module layering and compile-time dependency rules.
- **Central Package Management** — every NuGet version is pinned once in `Directory.Packages.props`.
- **Its own C# style** — `.editorconfig` plus six Roslyn rules for the layout options it has none
  for, enforced by `dotnet format` and the compiler in CI. See [docs/csharp-style.md](docs/csharp-style.md).
- **MVVM** with `CommunityToolkit.Mvvm`; **DI** with `Microsoft.Extensions.DependencyInjection`.
- A small, dependency-free, in-process **mediator** (commands/queries, pipeline behaviors, domain events).
- **xUnit v3** tests (unit, integration, and reflection-based **architecture** tests) with **NSubstitute**.
- **100% free / OSS** tool-chain — no Fluent Assertions, MediatR, AutoMapper, or other relicensed libraries.

## Prerequisites

- **.NET SDK 10.0.300** (pinned in `global.json`).
- **Windows 10 1809 (10.0.17763)** or later to build/run the WinUI host.
- For running the **unpackaged** app: the **Windows App Runtime** must be installed.
- Optional: **Visual Studio 2022 17.14+ / Visual Studio 2026** with the *Windows App SDK C# templates*
  for design-time XAML tooling. The CLI builds everything without Visual Studio.

## Solution layout

See [docs/architecture.md](docs/architecture.md) for the design and the rules.

## Getting started

```pwsh
# Restore + build everything (the solution maps the WinUI projects to a concrete platform for you)
dotnet build Kakehashi.slnx

# Run every test suite (unit + integration + architecture)
dotnet test Kakehashi.slnx

# Run the app (unpackaged). Requires the Windows App Runtime.
dotnet run --project src/App/Kakehashi.App/Kakehashi.App.csproj -p:Platform=x64
```

> Building the **WinUI executable on its own** requires a concrete platform, e.g.
> `dotnet build src/App/Kakehashi.App/Kakehashi.App.csproj -p:Platform=x64`.
> Building the **solution** does not, because the solution maps platforms automatically.

## Packaging

The host defaults to **unpackaged** (a plain `.exe`). Build a **packaged (MSIX)** app with:

```pwsh
dotnet build src/App/Kakehashi.App/Kakehashi.App.csproj -c Release -p:Platform=x64 -p:Packaged=true
```

A packaged build also needs image assets under `src/App/Kakehashi.App/Assets/` (Visual Studio adds
these automatically when you add a packaging project). The default unpackaged build needs no assets.

## Code style & quality gates

Consistency is enforced automatically — see the [CI workflow](.github/workflows/ci.yml):

| Gate | Mechanism |
| --- | --- |
| Formatting & naming | `.editorconfig` + `dotnet format --verify-no-changes` |
| Layout rules with no .editorconfig option | `client/tools/Kakehashi.Analyzers` (KH0001-KH0006), build errors |
| Compiler/analyzer warnings | `TreatWarningsAsErrors` in `Directory.Build.props` |
| Dependency rules | `Kakehashi.ArchitectureTests` (fails the build if a layer is crossed) |
| Package versions | Central Package Management (`Directory.Packages.props`) |

Run the style check locally before pushing:

```pwsh
dotnet format Kakehashi.slnx --verify-no-changes --severity warn
```

## Adding a new module

A feature usually spans both halves — a contract, a Go module that serves it, a WinUI module that
calls it. Use `/new-module`, which walks all three. The client-side shape is:

1. Three projects under `src/Modules/<YourModule>/`, dependency direction `UI → Application →
   Domain`. Never reference another module's projects.
2. Implement `IModule` in the UI project (register services, expose navigation items).
3. Declare the gateway port in Application; implement it in UI with the generated gRPC client.
4. Register the module in `src/App/Kakehashi.App/Composition/ModuleCatalog.cs`.
5. Add `*.Domain.Tests`, `*.Application.Tests`, `*.IntegrationTests` and `*.UI.Tests`, plus a
   `<Name>LayeringTests.cs` mirroring `AuthLayeringTests`.

The architecture tests will fail the build if the new module reaches across a boundary.

## Authentication (optional Auth module)

The client ships an optional **Auth module** (`src/Modules/Auth`) that signs users in with
**OpenID Connect / OAuth 2.0 Authorization Code + PKCE** through the system browser (RFC 8252), using
[`Duende.IdentityModel.OidcClient`](https://docs.duendesoftware.com/identitymodel-oidcclient/). It is
**inert until configured**: with an empty `Auth:Authority` the startup gate is skipped and the app runs
unauthenticated, exactly as if the module were absent.

### Configure

Point the `Auth` section of `src/App/Kakehashi.App/appsettings.json` at your OIDC provider:

```json
{
  "Auth": {
    "Authority": "https://your-issuer.example.com",
    "ClientId": "your-native-client-id",
    "Scope": "openid profile email roles offline_access api",
    "RedirectUri": "http://127.0.0.1:8765/"
  }
}
```

In Kakehashi the issuer is the Go server itself: point `Authority` at it (`http://localhost:8080` in
development) once the `identity` module is in place. Any other OIDC provider works too — Entra ID,
Auth0, Keycloak, Okta, or the public `https://demo.duendesoftware.com` — because the module only
speaks the standard. Register this app as a **public** (PKCE) client and allow the loopback redirect
URI above.
Once configured, the app requires sign-in before the shell appears, attaches the access token as a
bearer header to backend calls, persists the refresh token (DPAPI-encrypted, per user) so sign-in
survives restarts, and exposes an **Account** page to sign out. `offline_access` is required to receive
a refresh token.

### Remove it during setup

If your project does not need authentication, strip the module out completely:

```pwsh
pwsh scripts/configure-auth.ps1 -Remove
```

This deletes the Auth projects, solution entries, host registration, package pins and tests. The host's
authentication seams (the bearer-token handler and the gate orchestrator) stay in place but are inert,
so the build stays green. Re-enable by reverting the change in git.
