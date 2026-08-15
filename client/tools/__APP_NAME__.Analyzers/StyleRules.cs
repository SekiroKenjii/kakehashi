using Microsoft.CodeAnalysis;

namespace __ROOT_NAMESPACE__.Analyzers;

/// <summary>
/// The diagnostics for the layout rules that <c>dotnet format</c> has no option for.
/// </summary>
/// <remarks>client/docs/csharp-style.md</remarks>
public static class StyleRules
{
    /// <summary>The category every rule in this assembly reports under.</summary>
    public const string Category = "__APP_NAME__.Layout";

    /// <summary>A statement must be separated from the <c>return</c> that follows it.</summary>
    public static readonly DiagnosticDescriptor BlankLineBeforeReturn = new(
        id: "KH0001",
        title: "Blank line required before return",
        messageFormat: "Put a blank line before this 'return'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A return ends the method; the blank line is what makes it visible.");

    /// <summary>A statement must be separated from the <c>if</c> that follows it.</summary>
    public static readonly DiagnosticDescriptor BlankLineBeforeIf = new(
        id: "KH0002",
        title: "Blank line required before if",
        messageFormat: "Put a blank line before this 'if'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An if starts a branch; the blank line is where the straight line ends.");

    /// <summary>The namespace declaration is separated from the using directives above it.</summary>
    public static readonly DiagnosticDescriptor BlankLineBeforeNamespace = new(
        id: "KH0003",
        title: "Blank line required before the namespace declaration",
        messageFormat: "Put a blank line before the namespace declaration",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The namespace declaration stands alone between the usings and the first type.");

    /// <summary>The namespace declaration is separated from the first member below it.</summary>
    public static readonly DiagnosticDescriptor BlankLineAfterNamespace = new(
        id: "KH0004",
        title: "Blank line required after the namespace declaration",
        messageFormat: "Put a blank line after the namespace declaration",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The namespace declaration stands alone between the usings and the first type.");

    /// <summary>An interface with no members is written <c>interface IFoo;</c>.</summary>
    public static readonly DiagnosticDescriptor EmptyInterfaceBody = new(
        id: "KH0005",
        title: "Empty interface ends with a semicolon",
        messageFormat: "Interface '{0}' has no members; end it with ';' instead of '{{ }}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An empty body is two lines that say nothing a semicolon does not.");

    /// <summary>Every line is indented by whole units of four spaces.</summary>
    public static readonly DiagnosticDescriptor IndentationOffGrid = new(
        id: "KH0007",
        title: "Indentation is not a multiple of four",
        messageFormat: "This line is indented {0} spaces, which is not a whole number of levels",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "dotnet format does not own the indentation of a line it did not break, so a "
                + "continuation line can sit at any column and no other check will notice.");

    /// <summary>Two or more chained calls are written one per line, the dot leading.</summary>
    public static readonly DiagnosticDescriptor ChainedCallsOnePerLine = new(
        id: "KH0006",
        title: "Chained calls go one per line",
        messageFormat: "This chain has {0} calls; put each on its own line with the dot leading",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A chain read down a column shows what it does; read across a line it hides it.");
}
