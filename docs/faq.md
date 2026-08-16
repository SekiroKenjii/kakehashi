# FAQ

## Is this Windows only?

The **client** is. WinUI 3 is a Windows technology, and nothing here pretends otherwise.

The **server** is not. It compiles to one static Linux binary with no runtime dependencies, and the
contract between the two is protobuf over HTTP — so a web front end, a mobile app or a CLI can talk
to the same server without either half changing. `buf generate` will produce clients for whatever
language you point it at.

Development splits the same way: the server, the proto contract and the CLI all build on Linux and
macOS. Only building or running the client needs Windows.

A GTK or macOS client is a non-goal for this version. The architecture allows one — that is what the
three-layer split and the mediator are for — but shipping and maintaining one is a different project.

## Can I use PostgreSQL instead of SQL Server?

Not out of the box, and the work is bounded rather than trivial.

`platform/database` is one package with one driver, and each module's `store/` writes SQL against it.
The shape is right for a swap — nothing outside `store/` knows there is a database at all — but the
SQL itself is T-SQL: `@p1` placeholders, `OUTPUT INSERTED.Id`, `datetime2`, per-module schemas. That
is a rewrite of every `store/` package and every migration, not a configuration change.

If you are starting fresh and want PostgreSQL, change it before you have modules to rewrite: replace
the driver in `platform/database`, then port the example module's store and migrations. The gates
will not object — the boundary is exactly where you need it to be.

MongoDB is separate and optional; a project that never uses `platform/mongodb` can drop the service
from `docker-compose.yml` without touching anything else.

## Where does it deploy?

The server: anywhere that runs a Linux binary.

```sh
cd server && CGO_ENABLED=0 GOOS=linux go build -o app ./cmd/server
```

Copy it, point it at your SQL Server and MongoDB with the standard environment variables, and put a
reverse proxy in front that terminates TLS. There is a `Dockerfile` if you would rather ship a
container. TLS is not optional in production: the OpenID Connect endpoints handle credentials, and
browsers will refuse to send them over plain HTTP anyway.

The client: an `.exe` by default, or an MSIX with `-p:Packaged=true`. Unpackaged is the default
because it is the shorter path to running something; packaged is what you want for distribution,
auto-update and per-user install.

## Do I have to use the example module?

No, and it is meant to go.

- `kakehashi new --bare` never writes it.
- `kakehashi remove module notes` takes it back out later, wiring included.

See [remove-example.md](remove-example.md). Removing it leaves the frame with no feature module,
which is a supported state — the CI smoke job scaffolds both ways on every push.

## Why is the server its own identity provider?

Because the alternative for a small project is "bring your own Entra tenant", and that is a
prerequisite rather than a starting point. The account module is a real OpenID Connect provider
built on `zitadel/oidc`, so the flows are standard and swapping it for a hosted provider later means
pointing `Auth:Authority` somewhere else.

Sign-in defaults to in-app rather than the system browser. Handing the user to a browser is right
when the provider belongs to somebody else — the password is typed into *their* page and this
application never sees it. None of that holds when the provider is the same process the client
already talks to: the password crosses the same trust boundary either way, and the user pays a
window that steals focus and a loopback listener corporate firewalls dislike. `--auth browser`
switches it, and it is one line in `appsettings.json` afterwards.

## Why two version lines?

The CLI and the template ship separately, so they are tagged separately: `cli/vX.Y.Z` and
`template/vX.Y.Z`. A project made a year ago is on the template version it was made with, and
upgrading your CLI must not silently change what a generator writes into it.

Each side declares what it works with — the template's `requiresCli`, the CLI's supported template
range — and both are checked in both directions. `.kakehashi.json` records the pair, which is what
makes the check possible from inside a project that has no template tree to read.

## Can I upgrade a project to a newer template?

Not yet. `kakehashi upgrade` is designed and not implemented — see
[adr/0021](adr/0021-upgrade-is-a-three-way-merge.md).

What exists today is the groundwork it needs: `.kakehashi.json` records the template version and
every input the scaffold consumed, so a future upgrade can reproduce the old scaffold and the new
one and diff them. That is why the manifest belongs in version control.

In the meantime, a template release is not something you have to take. A scaffolded project is
yours; the CLI keeps working against it, and `add module` writes the shape that project already has.

## Why is the generated code committed?

So a fresh clone builds without `buf` installed. The cost is that it can drift, which is why
`buf generate && git diff --exit-code` is part of CI: regenerating has to change nothing.

## Do I have to keep the markers?

Yes, if you want `kakehashi add module` and `remove module` to keep working. A pair such as
`kakehashi:module-registrations:begin` and its `:end` is the generator's namespace, not the
application's, and it is how removal knows what to take back. Delete one and you have wiring that
outlives its module.

They are also the reason the CLI never rewrites a file it did not write: everything it touches in a
shared file is between two fences a human can see.

## Can I add my own gates?

Yes, and the two you would extend are data rather than code paths.

- **Server**: add a rule to `check()` in `server/tools/archlint/main.go`, with a test in both
  directions beside the others.
- **Client**: add a fact to the architecture test project. Per-module rules live with their module.

Resist proxies for the rules that are review conventions rather than facts — a line-count cap or a
mandatory `doc.go` fires on the files that are already right.

## Why WinUI 3 rather than WPF or Avalonia?

WinUI is the current Windows UI stack, Fluent by default, and it does not carry a decade of
compatibility. WPF is mature and going nowhere, but it is not where the platform is going. Avalonia
is the right answer if cross-platform is a requirement — and if it is, this template is the wrong
starting point, because half of it would be replaced.

## Is there a UI for the CLI?

The wizard is it: `kakehashi new` with no arguments. A separate GUI installer is a non-goal for this
version — the cost is high and a TUI covers the same ground.
