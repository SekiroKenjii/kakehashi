# C# style

`client/.editorconfig` is the style. This page states the parts a reader has to know and names
what enforces each one, because roughly half of them are not `.editorconfig` options at all.

## Layout

| | |
| --- | --- |
| Indent | 4 spaces (XAML: 4) |
| Column limit | 120 |
| Namespaces | file-scoped, with a blank line above and below |
| `using` | outside the namespace, `System.*` first, then alphabetical |
| Line endings | LF |

## Braces

**Allman for what declares or branches, K&R for what evaluates.**

```csharp
public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };

        if (text.Length == 0)
        {
            return;
        }

        var label = package.Kind switch {
            DataPackageKind.Text => "text",
            _ => "other",
        };

        Clipboard.SetContent(package);
    }
}
```

Four `csharp_new_line_before_open_brace` tokens carry the whole rule — `types`, `methods`,
`control_blocks`, `properties` — and every construct follows from one of them:

| Token | Covers |
| --- | --- |
| `types` | class, interface, struct, record, enum |
| `methods` | methods, constructors, finalizers, operators, **and local functions** |
| `control_blocks` | `if`/`else`, `for`, `foreach`, `while`, `do`, `try`/`catch`/`finally`, `switch` statement, `using`, `lock` |
| `properties` | the property, indexer and event **declaration** brace |

Left out, so they stay on the line that opens them: `accessors` (a `get`/`set` body), `lambdas`,
`anonymous_methods`, `anonymous_types`, and `object_collection_array_initializers` — that last one
also governs the **switch expression** and the property and recursive **patterns**, which is why it
has to stay out for those to read as expressions.

Two traps in that option. There is no token that moves a local function's brace on its own —
`local_functions` parses and does nothing, and `methods` is what governs them. And an
**unrecognised token is dropped silently**, with no diagnostic, so a typo reads as "this construct
is K&R" rather than as an error.

## The five rules with no `.editorconfig` option

These are Roslyn analyzers in `client/tools/__APP_NAME__.Analyzers`, each with a fixer in
`__APP_NAME__.Analyzers.CodeFixes`. `TreatWarningsAsErrors` makes every one of them a build error.

| ID | Rule |
| --- | --- |
| KH0001 | A blank line before `return` |
| KH0002 | A blank line before `if` |
| KH0003 | A blank line before the namespace declaration |
| KH0004 | A blank line after the namespace declaration |
| KH0005 | An interface with no members ends with `;`, not `{ }` |
| KH0006 | Two or more chained calls go one per line, the dot leading |
| KH0007 | Every line is indented by whole units of four spaces |

### Why KH0007 exists

`dotnet format` re-indents the lines it breaks and leaves every other line where it found it. A
continuation line inside an expression — an element of a collection expression, an argument on its
own line, the tail of a chain — can therefore sit at any column, and
`dotnet format --verify-no-changes` will still call the file clean. Verified by stripping the
indentation off such a file entirely: the formatter put back four spaces on the first element and
left the rest at column zero.

KH0007 is the only rule here that reads a line rather than a syntax node. It skips the inside of a
raw string, a verbatim string and a block comment, where leading spaces are the token's value.

### What the blank-line rules do not ask for

A statement that **opens** its block takes no blank line — there is nothing above it but the brace.
That covers a guard clause, the only statement in a method, and the first statement after a `case`
label. An `else if` is one branch of the statement above it, never a new one. A `return` inside an
expression-bodied member or a single-line lambda is not a statement at all.

When a comment is written directly above the statement, the blank line goes **above the comment**:

```csharp
var rows = await _admin.ListAsync(cancellationToken);

// The server's order is the tie-break, so a stable sort is required here.
return [.. rows.OrderBy(r => r.Order)];
```

### What KH0006 counts as a link

Two names ride on the call before them rather than forming a step of their own, and neither is
broken onto its own line:

- `ConfigureAwait` — ceremony for the awaiter.
- The NSubstitute verbs: `Returns`, `ReturnsForAnyArgs`, `ReturnsNull`, `ReturnsNullForAnyArgs`,
  `Throws`, `ThrowsAsync`, `ThrowsForAnyArgs`, `Received`, `ReceivedWithAnyArgs`, `DidNotReceive`,
  `DidNotReceiveWithAnyArgs` — the predicate of an arrange or an assert.

A member access that is not a call is not a link either, so `items.ToList().Count` is one call.

```csharp
services
    .AddHttpClient<IBackendClient, HttpBackendClient>(ConfigureClient)
    .AddHttpMessageHandler<BearerTokenHandler>();

_admins.ListUsersAsync(Arg.Any<CancellationToken>())
    .Returns(Result.Success(rows));
```

## Applying the rules to existing code

```bash
dotnet format analyzers __APP_NAME__.slnx --diagnostics KH0001 KH0002 KH0003 KH0004 KH0005 KH0006 --severity info
```

Run it more than once if a file is heavily affected, then `dotnet format whitespace __APP_NAME__.slnx`.

### CS2012 on the analyzers' own DLLs

Two separate causes, both handled:

1. **A stale compiler server.** `dotnet format` (or a previous build) leaves a compiler server
   holding the analyzer assemblies open, and the next build has to rewrite them. Release it first —
   CI does this between its format and build steps:

   ```bash
   dotnet build-server shutdown
   ```

2. **The same project built twice in one invocation.** Every reference to the analyzer projects
   pins `Platform=AnyCPU` (see `Directory.Build.props`). An undefined Platform and the solution's
   explicit `Platform=AnyCPU` are different global-property sets, so MSBuild used to build the same
   csproj twice into the same `obj\` — and the second compile raced the compiler server already
   holding the first DLL, failing a clean build nondeterministically. Keep the pin on any new
   reference to `client/tools`.

## What enforces what

| Rule | Enforced by |
| --- | --- |
| Indent, columns, braces, spacing, `using` order | `.editorconfig` + IDE0055, gated by `dotnet format --verify-no-changes` in CI |
| File-scoped namespaces | IDE0161 |
| Naming | IDE1006 |
| XML docs on the contract assemblies | CS1591 + `GenerateDocumentationFile` |
| KH0001–KH0006 | `client/tools/__APP_NAME__.Analyzers`, tested in `tests/__APP_NAME__.Analyzers.Tests` |
| Comment shape and length | `docs/COMMENTS.md` and the three checks in the `architecture` CI job |

Member order is a review convention, not tool-enforced: nested types → static/const/readonly
fields → fields and properties → constructors → methods; public before non-public within each
group.
