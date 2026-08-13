# Contributing

This repository follows [Gitflow](https://nvie.com/posts/a-successful-git-branching-model/).
**Nothing is committed to `main` directly.**

## The two long-lived branches

| Branch | Holds | Who merges into it |
| --- | --- | --- |
| `main` | What has been released. Every commit on it is tagged `vX.Y.Z`. | `release/*` and `hotfix/*`, through a pull request |
| `development` | Everything finished but not yet released. | `feature/*` and `bugfix/*`, through a pull request |

The model this repository follows names the production branch `master`. This one is **`main`** —
renaming it would break every existing clone, the remote's default, and the CI triggers, for a word.
Read `master` as `main` everywhere in the original article.

## The short-lived ones

| Prefix | Cut from | Merges back into | For |
| --- | --- | --- | --- |
| `feature/` | `development` | `development` | Anything new |
| `bugfix/` | `development` | `development` | A defect in unreleased work |
| `release/` | `development` | `main` **and** `development` | Preparing a version: bumping it, the changelog, last fixes |
| `hotfix/` | `main` | `main` **and** `development` | A defect in production that cannot wait for the next release |
| `support/` | a tag on `main` | itself | Keeping an old version alive |

A release or hotfix merges into **both** long-lived branches. Forgetting `development` is how a fix
ships and then comes back on the next release, so the pull request template asks about it.

## Day to day

```bash
git switch development && git pull
git switch -c feature/activity-export
# ... work, commit ...
git push -u origin feature/activity-export
gh pr create --base development --fill
```

The three gates in [CLAUDE.md](CLAUDE.md) must pass before the pull request is opened, not after —
CI runs the same ones and a red run is slower to read than a local failure.

Commit subjects follow Conventional Commits (`feat:`, `fix:`, `docs:`, …) — the table is in
[CLAUDE.md](CLAUDE.md). Comments follow [docs/COMMENTS.md](docs/COMMENTS.md): facts about the
current code, no history, long arguments in `docs/adr/`. Both are checked in review; the history
words additionally fail CI.

## Cutting a release

```bash
git switch development && git pull
git switch -c release/0.2.0
# bump the version, write the changelog, fix only what the release itself needs
gh pr create --base main --title "Release 0.2.0"
# after it merges:
git switch main && git pull
git tag -a v0.2.0 -m "..." && git push origin v0.2.0
gh pr create --base development --head main --title "Merge release 0.2.0 back into development"
```

## A hotfix

Cut from `main`, not from `development` — the point is to ship without the unreleased work:

```bash
git switch main && git pull
git switch -c hotfix/0.2.1-token-refresh
gh pr create --base main --title "Hotfix 0.2.1: ..."
# then tag main, and open the second pull request into development
```

## `git flow`, optionally

The commands above are plain git and always work. The [git-flow](https://github.com/petervanderdoes/gitflow-avh)
tool wraps them; it is **not** bundled with Git for Windows and has to be installed separately. This
repository is already configured for it, so `git flow feature start x` will use the right names
without asking:

```bash
git config gitflow.branch.master      main
git config gitflow.branch.develop     development
git config gitflow.prefix.feature     feature/
git config gitflow.prefix.bugfix      bugfix/
git config gitflow.prefix.release     release/
git config gitflow.prefix.hotfix      hotfix/
git config gitflow.prefix.support     support/
git config gitflow.prefix.versiontag  v
```

Those live in `.git/config`, which is not committed, so each clone runs them once. Note that
`git flow ... finish` deletes the branch and merges locally — with `main` protected, push the merge
through a pull request instead of letting the tool push it.

## What the protection rules enforce

`main` and `development` both refuse a direct push and require a pull request whose CI is green.
Administrators are deliberately **not** exempt from the review rules but can still push in a real
emergency; the intent is that the rule is the normal path, not a wall.
