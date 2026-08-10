# /remove-auth

Remove the optional **Auth (OpenID Connect)** module from the Kakehashi client during project setup,
for projects that do not need user sign-in.

The module ships enabled but **inert** — it does nothing until `Auth:Authority` is configured in
`client/src/App/Kakehashi.App/appsettings.json`. Removing it deletes the module and unwires it; the
host's authentication seams stay in place but are harmless.

> This removes the **client** half only. The server's `account` module is what issues the tokens; if
> nothing signs in, stop mounting it in `server/cmd/server/main.go` as well.

## Run

```powershell
pwsh client/scripts/configure-auth.ps1 -Remove
```

## What it removes

- The three source projects (`client/src/Modules/Auth/...`) and four test projects
  (`client/tests/Kakehashi.Modules.Auth.*`).
- `client/tests/Kakehashi.ArchitectureTests/AuthLayeringTests.cs`.
- The Auth entries in `client/Kakehashi.slnx`.
- The `Kakehashi.Modules.Auth.UI` project reference and the `new AuthModule()` registration in the
  host.
- The Auth project references in `Kakehashi.ArchitectureTests`.
- The OIDC/DPAPI package pins in `client/Directory.Packages.props`.
- The `Auth` section in `appsettings.json`.

Then it runs `dotnet build Kakehashi.slnx` to confirm the result is green (pass `-SkipBuild` to
skip).

## What it intentionally keeps

These cross-cutting seams are inert without the module, so removing them would touch the host for no
benefit:

- `IAccessTokenProvider` + `NullAccessTokenProvider` and the `BearerTokenHandler` / gRPC call
  credentials in `App.Infrastructure` — outbound backend calls simply go out unauthenticated.
- `IAuthenticationGate` + `AuthenticationOrchestrator` in the host — with no gate registered, startup
  proceeds straight to the shell.

## Re-enabling

Revert the change with git (the module is committed, so `git restore .` before committing — or
revert the commit afterwards — brings it back).
