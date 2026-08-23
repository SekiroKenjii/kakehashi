# Architecture Decision Records

One decision per file: Context → Decision → Consequences. Past tense belongs here — an ADR is where
a comment's history goes when the comment becomes present-tense fact, per
[docs/COMMENTS.md](../COMMENTS.md). Records are immutable once accepted; a reversal is a new record
that supersedes the old one.

**The shape of a new one**, which is what 0021 onwards do — an em dash after the number, and no
`Date:` or `Status:` line, because git records both:

```markdown
# 0028 — The decision, as a sentence in the present tense

## Context
## Decision
## Consequences
```

Around thirty lines, and up to seventy where the alternatives were worth rejecting in writing. The
shortest record here is 29 and the longest is 72; the number is descriptive rather than a target,
and one that fits in ten lines is usually a comment.

0001–0020 carry `# 0001. Title` — a full stop after the number — with `Date:` and `Status:` lines
under it. That is the older form, kept because a record is immutable, and it is not the one to copy.

| # | Decision |
| --- | --- |
| [0001](0001-per-route-permission-policy.md) | Every route declares its own access policy |
| [0002](0002-permission-catalogue-from-gating-modules.md) | The permission catalogue lists only permissions some route checks |
| [0003](0003-signedout-vs-sessionrevoked.md) | Signing out and being revoked are two events |
| [0004](0004-staged-edits-atomic-apply.md) | Admin edits are staged, then applied atomically |
| [0005](0005-scope-order-is-not-string-order.md) | Row scopes rank by explicit order, not string sort |
| [0006](0006-id-token-is-not-an-access-token.md) | An ID token cannot authenticate an API call |
| [0007](0007-in-app-sign-in-alongside-browser-oidc.md) | In-app sign-in issues tokens through the same OIDC provider |
| [0008](0008-hand-rolled-file-logger.md) | The file logger is hand-rolled rather than a logging package |
| [0009](0009-centralized-grpc-client-registration.md) | gRPC clients are registered in one place, address read at resolve time |
| [0010](0010-startup-orchestrator-ordering.md) | Startup is ordered orchestrators, and the order is load-bearing |
| [0011](0011-pages-load-on-loaded-not-onnavigatedto.md) | Pages load on `Loaded`, not `OnNavigatedTo` |
| [0012](0012-wrappanel-over-uniformgridlayout.md) | Chips wrap with a custom `WrapPanel`, not `UniformGridLayout` |
| [0013](0013-client-owned-icon-vocabulary.md) | The server sends icon names; each client owns its glyph mapping |
| [0014](0014-issuedtoken-is-not-an-aggregate-root.md) | `IssuedToken` lives inside `UserSession`, not as its own root |
| [0015](0015-module-attachment-is-not-a-security-boundary.md) | Attach/detach is composition preference; the server enforces access |
| [0016](0016-one-example-module-in-the-template.md) | The template ships one example module, and it is Notes |
| [0017](0017-oidc-provider-is-core.md) | The OpenID Connect provider is CORE; its seed values are configuration |
| [0018](0018-database-driven-navigation-stays.md) | Database-driven navigation stays CORE; only its editor leaves |
| [0019](0019-cli-lives-in-the-monorepo.md) | The CLI lives in this repository, versioned by tag prefix |
| [0020](0020-no-second-example-module.md) | No second example module; Notes already touches the event bus |
| [0021](0021-upgrade-is-a-three-way-merge.md) | `upgrade` reconstructs both versions and patches the difference |
| [0022](0022-cli-tags-carry-the-module-path.md) | The CLI's tags carry its module path, so `go install` can read them |
| [0023](0023-plugins-load-at-startup.md) | Plugins load during host construction, and the disk settles before they do |
| [0024](0024-plugin-xaml-resolves-through-a-runtime-loaded-pri.md) | Plugin XAML resolves through a runtime-loaded PRI and a metadata bridge we own |
| [0025](0025-a-plugins-declared-permission-is-disclosure.md) | A plugin's declared permission is disclosure, not a gate |
| [0026](0026-plugin-artifacts-stream-over-the-contract.md) | Plugin artifacts stream over the contract, not from a static route |
| [0027](0027-an-install-is-reported-through-the-module-that-published-it.md) | An install is reported through the module that published it |
