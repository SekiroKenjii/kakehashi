# Comments — the convention

A comment states a fact about the current code: a constraint, an invariant, a consequence of
breaking it, or a technical reason that cannot be read off the code. Nothing else.

## The rules

1. **Facts, not narrative.** No rhetoric, no metaphors, no dialogue with an imagined reviewer.
   The three-second test: after reading a comment you can answer *"what does this require or
   forbid?"* If you cannot, it is prose — cut it.

2. **Present tense, current code only.** What the code used to do, and why it changed, belongs in
   the PR description or an ADR under `docs/adr/`. A comment containing *used to*, *no longer*,
   *the old*, *until now* or *previously* fails CI. Never quote an old comment inside a new one.

3. **Density follows danger.** Comment heavily where a mistake is expensive and invisible:
   - `api/` packages and `Kakehashi.UI.Contracts` / `*.Application.Abstractions` — the contracts
     other modules build against;
   - anything that crosses the wire (renaming breaks deployed clients — say so, in one sentence);
   - security decisions (allow-lists, policies): what is permitted, what the server decides,
     and the question to answer before extending;
   - platform traps (thread affinity, h2c timeouts, index renaming, PUA glyphs).

   Do **not** comment members whose name already says everything. An `[ObservableProperty]` flag,
   a CRUD property, or a private field gets a doc comment only when it carries an invariant.

4. **Long comments are misplaced documents.** A block over ~6 lines is architecture documentation
   wearing a comment costume. Move it to `docs/` or `docs/adr/NNNN-<slug>.md`
   (Context → Decision → Consequences, 10–20 lines) and leave one sentence plus the link.

5. **Docs are the source of truth, not comments.** A file in `docs/` never cites "the comment
   above X" as authority; the dependency points the other way.

## Shape

**Go.** Package doc: 1–3 sentences — what the package is, plus its most important import
constraint. Exported identifiers: godoc form, starting with the identifier's name, 1–2 sentences.
`api/` packages may run longer; they still state facts.

**C#.** `<summary>` is one sentence. `<remarks>` only for a real invariant or contract, at most
~4 lines. XML docs are required (CS1591) only in the contract assemblies; elsewhere they are
earned, not default.

**Inline comments** (both halves): 1–3 lines, leading with the fact.

## Examples

Wrong — narrative, history, and a trivial member:

```csharp
/// <summary>Whether the pane preview drawer is showing.</summary>
/// <remarks>
/// Closed to begin with. The preview answers a question somebody asks now and
/// then, so it costs the two editing columns nothing until it is asked.
/// </remarks>
[ObservableProperty]
public partial bool IsPreviewOpen { get; set; }
```

Right — the name is the documentation:

```csharp
[ObservableProperty]
public partial bool IsPreviewOpen { get; set; }
```

Wrong — a fact buried in an afternoon's worth of prose:

```go
// Without h2c, both cases fail with an error about the protocol rather than
// about the missing TLS, which is a confusing way to spend an afternoon.
```

Right:

```go
// gRPC requires HTTP/2 and net/http only negotiates HTTP/2 over TLS; behind the
// TLS-terminating proxy (and in development) this server speaks cleartext, so it
// serves h2c.
```

Right — a wire contract, said once:

```go
// Feed entry kinds. These strings cross the wire and clients switch on them;
// renaming one breaks deployed clients. Treat as contract.
```

## Enforcement

- `revive` (`exported`, `package-comments`) via `golangci-lint`, configured in
  `server/.golangci.yml` and run by the `server` CI job — godoc shape for exported symbols.
  Methods on unexported types are not checked: they implement generated interfaces, and a doc
  there could only restate the signature.
- History-marker check in the `architecture` CI job: the rule-2 words fail the build in
  `server/internal/modules` and `client/src`, excluding `gen`, `obj` and `bin` — generated gRPC
  stubs carry proto comments this rule does not govern.
- CS1591 + `GenerateDocumentationFile` on the contract assemblies only:
  `Kakehashi.UI.Contracts` and `Kakehashi.Application.Abstractions`. Not `Kakehashi.Contracts`,
  which holds generated code.
- Everything else is review. When you touch a file for another reason, comments inside your
  change conform to this document; leave the rest alone.
