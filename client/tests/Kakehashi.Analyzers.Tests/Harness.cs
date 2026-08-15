using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kakehashi.Analyzers.Tests;

/// <summary>
/// Runs an analyzer over a source string and returns what it reported.
/// </summary>
/// <remarks>
/// Every rule in this assembly is syntactic, so the compilation does not have to resolve: the
/// references are the running framework's, and any symbol the source does not declare is simply
/// unresolved and irrelevant to a syntax-node action.
/// </remarks>
internal static class Harness
{
    private static readonly ImmutableArray<MetadataReference> _references = Load();

    /// <summary>The lines each diagnostic was reported on, 1-based, in order.</summary>
    internal static async Task<int[]> LinesAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var diagnostics = await ReportAsync(analyzer, source);

        return [.. diagnostics
            .Select(d => d.Location.GetLineSpan().StartLinePosition.Line + 1)
            .OrderBy(line => line)];
    }

    internal static async Task<ImmutableArray<Diagnostic>> ReportAsync(
        DiagnosticAnalyzer analyzer, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            "Probe",
            [tree],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<MetadataReference> Load()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var paths = trusted.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var references = new List<MetadataReference>(paths.Length);

        foreach (var path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return [.. references];
    }
}
