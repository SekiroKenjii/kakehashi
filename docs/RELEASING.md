# Releasing

This repository publishes two things on two version lines. They are tagged separately, released by
separate workflows, and have separate changelogs, because they ship separately and a project is on
the template version it was made with.

| | Tag | Changelog | Workflow |
| --- | --- | --- | --- |
| Template | `template/vX.Y.Z` | [CHANGELOG.template.md](../CHANGELOG.template.md) | `.github/workflows/release-template.yml` |
| CLI | `tools/cli/vX.Y.Z` | [CHANGELOG.cli.md](../CHANGELOG.cli.md) | `.github/workflows/release-cli.yml` |

The CLI's prefix is its own directory because `tools/cli` is a Go module and that is the only prefix
`go install` reads — ADR 0022.

Neither workflow fires on a branch. There are two ways to release, and they end in the same place:

| | How | The tag |
| --- | --- | --- |
| **From Actions** | Run the workflow, give it the version, turn **dry run off** | the release creates it, at `main` |
| **From a tag push** | `git tag` and `git push` | you create it, and the push is the trigger |

The first is fewer steps and nothing to forget; the second is what a scripted release does. Both run
the same checks and produce the same assets.

Leaving **dry run on** — the default — builds and checks everything a release would carry, uploads
it to the workflow run, and creates no tag and no release. Do that first.

Publishing from Actions only runs on `main`; the workflow refuses any other ref, because `main` is
the only branch that holds what was released. A dry run may rehearse from anywhere, which is what a
release branch wants.

## What the numbers mean

**Template.** MAJOR when the structure, the markers or the unit format changed and an older CLI
cannot read this template. MINOR when something was added. PATCH for a defect in the template.

**CLI.** Ordinary semantic versioning for a tool.

The two are held together by a compatibility matrix, stated from both sides and checked in both
directions:

| Where | Says |
| --- | --- |
| `templates/template.json` → `requiresCli` | the CLI range this template needs |
| `template.SupportedTemplates` in the CLI | the template range this binary understands |

`new` checks both against the template it resolved. `add` and `remove` check both against
`.kakehashi.json`, which records the template's `requiresCli` at scaffold time. A refusal names the
side that has to move.

**Raising either bound is the decision, not the tag.** A template that raises `requiresCli` cuts off
every older CLI; a CLI that narrows `SupportedTemplates` cuts off every older project. Do it when
the format actually changed, and say so in the changelog.

---

## Releasing the template

1. **Decide the number**, and put it in `templates/template.json` as `templateVersion`. The release
   workflow refuses to build if the tag and the descriptor disagree.
2. **Raise `requiresCli`** only if this template needs a CLI that older ones are not.
3. **Bump the schemas** — `markersSchema`, `unitsSchema` — if the marker vocabulary or the unit
   format changed. A format that changes without its number is what breaks a future `upgrade`
   (ADR 0021).
4. **Write the changelog entry** in `CHANGELOG.template.md`, for somebody deciding whether to take
   the release rather than for somebody reading commits.
5. **Dry run.** Actions → Release template → Run workflow, version `X.Y.Z`, dry run on. It runs the
   whole smoke suite on both operating systems, packages the asset, scaffolds a project *from the
   asset*, builds it, and uploads the archive and its checksums to the run. Download them and look.
6. **Merge to `main`** through the usual release branch — see [CONTRIBUTING.md](../CONTRIBUTING.md).
7. **Publish**, either way:

   Actions → Release template → Run workflow, **from `main`**, version `X.Y.Z`, dry run **off**. The
   release creates the tag.

   ```sh
   git switch main && git pull
   git tag -a template/vX.Y.Z -m "Template vX.Y.Z"
   git push origin template/vX.Y.Z
   ```

8. **Check the release.** The asset is `template-vX.Y.Z.tar.gz` with `checksums.txt` beside it. Then
   scaffold from it for real, with no `--template-dir`:

   ```sh
   kakehashi new ReleaseCheck --module example.com/releasecheck --no-input
   ```

### What the asset contains

The tracked files at that commit, minus every path `templates/template.json` lists as belonging to
the template repository — `tools/cli`, `docs/pivot`, `docs/BOILERPLATE.md`, the packaging manifests,
and the rest. The descriptor itself stays: the CLI reads it out of the archive.

That trim is driven by the descriptor rather than by a list in the workflow, so there is one
statement of what is template-only and the scaffold and the packaging cannot disagree about it.

---

## Releasing the CLI

1. **Bump `version` in `tools/cli/internal/cli/cli.go`** to match. The release build injects the
   version with `-ldflags`, so a downloaded binary reports whatever the workflow passed it — but
   `go install` injects nothing and reports the source value. A tag that disagrees with `cli.go`
   ships the P0 channel calling itself the wrong version, and the compatibility matrix is decided on
   what the binary says it is. The workflow refuses the mismatch.
2. **Write the changelog entry** in `CHANGELOG.cli.md`, including the template range this release
   supports.
3. **Dry run**, locally — this is the same script CI runs, which is the point of it being a script:

   ```sh
   cd tools/cli && ./scripts/build-release.sh X.Y.Z
   ```

   Six archives and a `checksums.txt`. Check one:

   ```sh
   cd dist && sha256sum -c checksums.txt
   tar -xzf kakehashi_X.Y.Z_Linux_amd64.tar.gz -C /tmp && /tmp/kakehashi version
   ```

   Or Actions → Release CLI → Run workflow, dry run on, and download the artifact.
4. **Merge to `main`.**
5. **Publish**, either way:

   Actions → Release CLI → Run workflow, **from `main`**, version `X.Y.Z`, dry run **off**. The
   release creates the tag.

   ```sh
   git switch main && git pull
   git tag -a tools/cli/vX.Y.Z -m "CLI vX.Y.Z"
   git push origin tools/cli/vX.Y.Z
   ```

6. **Check both channels.** The version query has no prefix: the tag carries the module's directory
   and `go install` takes what is left.

   ```sh
   go install github.com/SekiroKenjii/kakehashi/tools/cli/cmd/kakehashi@vX.Y.Z
   go install github.com/SekiroKenjii/kakehashi/tools/cli/cmd/kakehashi@latest
   kakehashi version
   ```

   `@latest` is the one that proves the tag is shaped right. If it installs a `v0.0.0-`
   pseudo-version, Go did not recognise the tag and is serving the default branch instead.

   and download one archive from the release and check it against `checksums.txt`.

7. **Update the packaging manifests** — [packaging/](../packaging/) — with the version and the
   digests from `checksums.txt`, and submit them. Not automated, and not CI's to do: both go to
   somebody else's repository.

---

## Launch checklist

The one-time list for the first public release. Everything above is per-release; this is not.

- [ ] **Full pipeline on a clean Windows machine.** A fresh VM with nothing but the prerequisites:
      `doctor` → `new` → `docker compose up -d` → run the client → `add module` → the three gates →
      `remove module`. This is the only check that covers what a first-time reader actually meets.
- [ ] **A screenshot of the Home page** and one of the wizard, in the README. The Home page is the
      pitch: the checklist ticking itself is the thing that is hard to explain in prose.
- [ ] **Repository topics**: add `scaffolding`, `code-generator`, `cli`, alongside the existing
      `winui`, `golang`, `grpc`.
- [ ] **Repository description**: lead with "boilerplate + CLI", not "an app and its server". The
      old description describes what this repository used to be.
- [ ] **Turn on "Use this template"** in the repository settings, and check the README says what to
      run afterwards — `tools/rename/rename.ps1` — for somebody who arrives that way.
- [ ] **Turn on Discussions**, which the issue-template config links to.
- [ ] **First release**: template then CLI, in that order. The CLI's default resolution
      needs a published template to find, so a CLI release without one is a tool with nothing to
      fetch.
- [ ] **Check the compatibility refusals against the real releases**, in both directions: an old CLI
      against the new template, and this CLI against a template whose `requiresCli` excludes it.
- [ ] **`go install …@latest`** from a machine that has never seen this repository.
- [ ] Optional: a write-up on dev.to or r/csharp and r/golang. The three gates and `add module`
      end to end are the differentiators; the stack is not.
