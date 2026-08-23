using System;
using System.Collections.Generic;
using System.IO;
using __ROOT_NAMESPACE__.PluginSdk.Abstractions;
using __ROOT_NAMESPACE__.SharedKernel;

namespace __ROOT_NAMESPACE__.PluginTool;

/// <summary>
/// Two verbs over the SDK's packaging library, and nothing else.
/// </summary>
/// <remarks>
/// Deliberately thin: every rule it reports comes from
/// <see cref="PluginContentValidator"/> and <see cref="PluginPackage"/>, which the application runs
/// as well. A check implemented here would be one an author passes and a user's installation still
/// refuses.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }

        return args[0] switch {
            "validate" => Report(Validate(args[1])),
            "pack" => Pack(args[1], args.Length > 2 ? args[2] : args[1]),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Checks a project directory or a packed file, and returns everything wrong with it.
    /// </summary>
    /// <remarks>
    /// A project is checked by packing it in memory first, so what this approves is the archive
    /// <c>pack</c> would have written rather than a directory that resembles one.
    /// </remarks>
    private static IReadOnlyList<Error> Validate(string subject)
    {
        if (File.Exists(subject) && subject.EndsWith(PluginPackage.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var opened = PluginPackage.Open(subject);

            if (opened.IsFailure)
            {
                return [opened.Error];
            }

            using var package = opened.Value;

            Console.WriteLine("Markup was not checked: a packed plugin holds no .xaml.");

            return [.. package.Validate(), .. PluginContentValidator.Validate(package)];
        }

        if (!Directory.Exists(subject))
        {
            return [PluginErrors.ProjectManifestMissing(subject)];
        }
        var built = PluginPackager.Build(subject);

        if (built.IsFailure)
        {
            return [built.Error];
        }

        using var archive = built.Value;
        var fromStream = PluginPackage.Open(archive, leaveOpen: true);

        if (fromStream.IsFailure)
        {
            return [fromStream.Error];
        }

        using var project = fromStream.Value;

        return [
            .. project.Validate(),
            .. PluginContentValidator.Validate(project),
            .. PluginContentValidator.ValidateMarkup(subject),
        ];
    }

    private static int Pack(string projectDirectory, string outputDirectory)
    {
        var packed = PluginPackager.Pack(projectDirectory, outputDirectory);

        if (packed.IsFailure)
        {
            return Report([packed.Error]);
        }
        Console.WriteLine(packed.Value);
        Console.WriteLine(packed.Value + PluginPackager.DigestExtension);

        return 0;
    }

    private static int Report(IReadOnlyList<Error> errors)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"{error.Code}: {error.Message}");
        }

        if (errors.Count > 0)
        {
            return 1;
        }
        Console.WriteLine("No problems found.");

        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: __APP_NAME_LOWER__-plugin validate <project|package>");
        Console.Error.WriteLine("       __APP_NAME_LOWER__-plugin pack <project> [output]");

        return 2;
    }
}
