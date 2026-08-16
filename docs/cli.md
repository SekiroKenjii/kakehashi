# The CLI

One binary. `new` makes a project from the template; `add` and `remove` maintain one afterwards;
`doctor` checks the machine.

Install it with `go install`, or take a binary and its checksum from the boilerplate's releases page
— the one-line form of both is in the boilerplate's own README. The CLI is tagged `tools/cli/vX.Y.Z`,
separately from the template's `template/vX.Y.Z`, and the two are released independently.

## Exit codes

| Code | Means |
| --- | --- |
| `0` | it worked |
| `1` | it did not work |
| `2` | you asked for the wrong thing — the usage is printed |

The two are separated because a script that wraps this tool treats them differently.

---

## `kakehashi new [app-name]`

Scaffolds a project. With no app name it opens the wizard; with one it takes the rest from flags.

```sh
kakehashi new                                                    # the wizard
kakehashi new OrderDesk --module github.com/you/orderdesk        # flags
```

| Flag | Default | |
| --- | --- | --- |
| `--module` | — | **required** without the wizard. The server's Go module path. |
| `--title` | the app name | What the window and the Home page call it. |
| `--proto-package` | the app name, lower case | The proto root package. |
| `--accent` | `#E34234` | Six hex digits after a hash. |
| `--author` | `git config user.name` | Copyright holder. |
| `--auth` | `inapp` | `inapp` or `browser`. |
| `--with-example` | `true` | Keep the example module. |
| `--bare` | `false` | Leave it out. Contradicts `--with-example=true`. |
| `--dir` | `./<app-name lower case>` | Destination. Must be absent or empty. |
| `--template-version` | the newest compatible release | |
| `--template-dir` | — | Scaffold from a checkout instead of a release. |
| `--offline` | `false` | Use the template cache and never the network. |
| `--dry-run` | `false` | Do the whole scaffold in a temporary directory and throw it away. |
| `--no-input` | `false` | Never prompt: fail instead of opening the wizard. |

**The wizard** asks seven questions, one per screen, each with a default but the app name. It needs
a terminal it can prompt; a pipe, a redirect or a CI runner gets a refusal naming the flags to pass
instead, and exit code 2. `--no-input` asks for that refusal deliberately.

**Before anything is downloaded** the command checks the destination and runs the machine checks
scaffolding itself depends on. Finding out afterwards would cost a download and an extraction.

**Nothing partial is left behind.** Every step happens in a temporary directory beside the
destination, and the destination appears in one rename at the end.

### What resolution picks

With no `--template-version`, the newest release **this CLI is compatible with** — not simply the
newest. A template declares the CLI range it needs and this CLI declares the template range it
understands; resolution walks back until both hold, so an older CLI keeps working after a template
raises its floor. Both directions are checked, and a refusal names the side that has to move.

Downloads are verified against the release's own `checksums.txt` and cached, so a second scaffold of
the same version needs no network.

---

## `kakehashi add module <id>`

Generates a module across both halves, wired in, with all three gates green.

```sh
kakehashi add module orders
kakehashi add module people --entity Person
```

| Flag | Default | |
| --- | --- | --- |
| `--entity` | the singular of the id | The aggregate's type name. |
| `--icon` | `document` | A name from the client's icon vocabulary. |
| `--crud` | `true` | The CRUD slice end to end. |
| `--store` | `sql` | Where the module keeps its data. |
| `--no-client` | `false` | The proto and the server half only. |
| `--no-page` | `false` | The client module without a page. |
| `--dry-run` | `false` | Print the plan and write nothing. |

The id is the package, the SQL schema and the proto directory: lower case, no separators, and not
one of the names the layout already gives a meaning. Every other spelling is derived from it — see
[first-module.md](first-module.md).

The generator's templates are derived from the example module and a test asserts the round trip, so
what `add module` writes is what the template ships, by construction rather than by discipline.

---

## `kakehashi add page <module> <PageName>`

A page inside a module that already exists. Client only.

```sh
kakehashi add page orders Archive
```

| Flag | Default | |
| --- | --- | --- |
| `--title` | the name, spaced | What the navigation pane shows. |
| `--no-nav` | `false` | Register the page without a navigation entry. |
| `--dry-run` | `false` | Print the plan and write nothing. |

The name is PascalCase and without the word `Page`: `Archive` gives `ArchivePage` and
`ArchivePageViewModel`.

---

## `kakehashi remove module <id>`

Takes a module back out: its paths, and the wiring between the marker fences in every file that
knows about modules.

```sh
kakehashi remove module notes
```

| Flag | Default | |
| --- | --- | --- |
| `--dry-run` | `false` | Print the plan and remove nothing. |
| `--force` | `false` | Remove even though the working tree has other changes in it. |

What comes out is what the record says went in: `.kakehashi/units/<id>.json` for a generated module,
`templates/units/<id>.json` for one the template shipped. A module with no record cannot be removed
— and saying so is better than guessing at paths.

---

## `kakehashi doctor`

Checks the machine for what building a scaffolded project needs, and prints the command that fixes
whatever is missing.

```sh
kakehashi doctor
kakehashi doctor --json
```

Some checks are required for scaffolding and some only for building; `new` runs the required subset
before it downloads anything.

---

## `kakehashi version`

The CLI's version, and the template releases in the local cache.

---

## Where things live

| | |
| --- | --- |
| `.kakehashi.json` | at the project root: the template version, its `requiresCli`, and every input the scaffold consumed |
| `.kakehashi/units/` | one record per generated module, read by `remove module` |
| `templates/units/` | the same, for modules the template shipped |
| the template cache | outside the project, under the user's cache directory; `kakehashi version` prints the path |

`.kakehashi.json` is the only file in a scaffolded project allowed to name the generator, and it is
what a future `upgrade` will read to reproduce the scaffold. Keep it in version control.
