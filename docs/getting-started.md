# Getting started

From nothing to a Windows app talking to its own server. Fifteen minutes on a cold machine, most of
it waiting for downloads.

## What you need

| | Why |
| --- | --- |
| **Windows 10 2004+ / 11** | the client is WinUI 3; the server is not, and builds anywhere |
| **.NET 10 SDK** | the client |
| **Go 1.26+** | the server, and the CLI |
| **[buf](https://buf.build/docs/installation)** | the contract between them |
| **Docker Desktop** | SQL Server and MongoDB, in one compose file |
| **Windows App Runtime** | running the client unpackaged |

`kakehashi doctor` checks all of it and prints the command that fixes anything missing. Run it
first; it is faster than finding out during a build. If you do not have the CLI yet, the
boilerplate's own README carries the one-line install.

```sh
kakehashi doctor
```

## 1. Scaffold

```sh
kakehashi new
```

With no arguments this opens a wizard: app name, display title, Go module path, whether to keep the
example module, sign-in mode, accent. Every question but the first has a default, so pressing Enter
through the rest gives you a working project. The last screen is a summary and the destination —
Enter runs it, Escape leaves without writing anything.

If you would rather not be asked, or you are in CI:

```sh
kakehashi new OrderDesk --module github.com/you/orderdesk --no-input
```

Either way the pipeline reports its stages, and what comes out is a git repository with one commit
on `main`.

```text
OrderDesk <github.com/you/orderdesk>
  ✓ fetch
  ✓ verify
  ✓ apply
  ✓ check
  ✓ git
```

## 2. Start the backend

```sh
cd orderdesk
docker compose up -d
curl http://localhost:8080/healthz
```

The first `up` pulls SQL Server and MongoDB, which is the long part. `healthz` answers 200 once the
server has run its migrations; until then it will refuse the connection, which is not an error, only
an ordering.

## 3. Run the client

```pwsh
dotnet run --project client/src/App/OrderDesk.App/OrderDesk.App.csproj -p:Platform=x64
```

`-p:Platform=x64` is not optional when you build the WinUI project directly — building the solution
sets it, building one project does not.

## 4. Read the Home page

This is the part worth slowing down for. The Home page is written to be the first documentation you
meet rather than a splash screen.

- **Backend** — the endpoint, the protocol and whether anything answered. If it reads *Offline*, the
  card carries `docker compose up -d` with a copy button and a **Retry** beside it. Nothing else has
  to be true for the client to start; it simply reports what it found.
- **Getting started** — a checklist that reads real state. The backend row ticks when the server
  answers. The example module's row ticks when there is a note in the database — which means writing
  one is the fastest way to prove the whole path works, from a WinUI page through the mediator, over
  gRPC, into a SQL Server table. The remaining rows are commands with a copy button.
- **The three gates** — what each protects and how to run it. Running them once now is how they stop
  being a surprise in your first pull request.

## 5. Write a note

Open **Notes** from the navigation pane and create one. When you come back to Home, the second row
of the checklist is ticked.

That round trip is the whole architecture in one action:

```text
NotesPage.xaml        x:Bind, no code-behind logic
NotesViewModel        ISender.Send(CreateNoteCommand)
CreateNoteHandler     validates the draft locally, then calls the gateway port
GrpcNotesGateway      the only place that knows about gRPC
  ── the wire ──      proto/<pkg>/notes/v1/notes.proto
notes/rpc             the only server package that may import generated code
notes/service         the use case
notes/domain          the invariant that refused a blank title on the client, again
notes/store           notes.Note, in the module's own SQL schema
```

Neither half can reach past those boundaries; that is what the gates are for.

## Where next

- [first-module.md](first-module.md) — the same slice, but yours
- [gates.md](gates.md) — what each gate refuses, and how to read what it prints
- [remove-example.md](remove-example.md) — when Notes has served its purpose
- [cli.md](cli.md) — every command and flag
- [ARCHITECTURE.md](ARCHITECTURE.md) — why the shape is the shape

## When it does not work

**The CLI is not found.** `go install` puts binaries in `$(go env GOPATH)/bin`, which has to be on
`PATH`.

**The wizard refuses to open.** It needs a terminal that can prompt. Piped input, a CI runner or a
redirected stdout all get a refusal that names the flags to pass instead, and a non-zero exit code.

**`healthz` refuses the connection.** SQL Server takes a while on first start. `docker compose logs
-f server` shows the migrations running.

**The Backend card says Offline but `curl` works.** The client reads `Backend:BaseAddress` from
`client/src/App/<App>.App/appsettings.json`. A committed `appsettings.json` with no `Backend`
section shows *Not configured* rather than *Offline* — that is the difference between the two.

**The client will not build.** Build the solution rather than the project, or pass
`-p:Platform=x64`.
