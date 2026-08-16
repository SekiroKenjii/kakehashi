# Packaging

The manifests for the channels that need one of their own. `go install` needs nothing, and the
GitHub release is produced by `.github/workflows/release-cli.yml`.

Neither of these is published by CI. Both carry a version and a digest that only exist once a
release does, and both are submitted to somebody else's repository — a step that should be a
person's, at least until the CLI has a release cadence worth automating.

| Channel | Priority | Where it goes |
| --- | --- | --- |
| [`winget/`](winget/) | P1 | a pull request to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) |
| [`scoop/`](scoop/) | P2 | a bucket of our own, or a pull request to `ScoopInstaller/Extras` |

## Filling them in

After `tools/cli/vX.Y.Z` is published, its `checksums.txt` has every digest these files need.

```sh
version=1.0.0
gh release download "tools/cli/v$version" --pattern checksums.txt --output - | grep -i windows
```

Then replace `__VERSION__` and each `__SHA256_*__` in the copies you submit. Leaving the
placeholders in the repository is deliberate: a manifest with a stale digest that looks filled in is
worse than one that obviously is not.

## winget

Three files, in the layout winget-pkgs expects under
`manifests/s/SekiroKenjii/Kakehashi/<version>/`. The package is a **portable** install: the CLI is
one binary with no installer, so winget puts it on `PATH` and does not run anything.

Validate before submitting:

```pwsh
winget validate --manifest packaging/winget
winget install --manifest packaging/winget   # installs the local build
```

## scoop

One JSON manifest. `autoupdate` is filled in, so a bucket picks up later releases without another
pull request; the first one still has to be by hand.

```pwsh
scoop install packaging/scoop/kakehashi.json
```
