using System;
using System.IO;
using System.Security.Cryptography;
using __ROOT_NAMESPACE__.Interoperability;

namespace __ROOT_NAMESPACE__.App.Plugins;

/// <summary>How much a package is trusted, in the two words the install prompt uses.</summary>
public enum PluginTrustLevel
{
    /// <summary>
    /// Signed, unmodified since, and by the same publisher as this application. Installs without a
    /// prompt.
    /// </summary>
    Verified,

    /// <summary>Everything else, including a valid signature from somebody else.</summary>
    Unofficial,
}

/// <summary>What is known about a package before it is installed.</summary>
/// <param name="Level">Verified or unofficial.</param>
/// <param name="Signature">What the trust provider concluded about the entry assembly.</param>
/// <param name="Signer">The signer's certificate subject, empty when unsigned.</param>
/// <param name="SHA256">The digest of the package file, lower-case hex.</param>
public sealed record PluginTrustVerdict(
    PluginTrustLevel Level, SignatureStatus Signature, string Signer, string SHA256);

/// <summary>
/// Decides how far to trust a package.
/// </summary>
/// <remarks>
/// Two questions, and they answer different things. The digest identifies the exact bytes, which is
/// what consent is recorded against and what a catalog download is checked against. The signature
/// says whether those bytes were vouched for and by whom, which is what decides whether a user is
/// asked at all.
/// <para>
/// Verified means the same publisher as this application, not merely a valid signature: a package
/// signed by somebody else is signed by somebody else, and calling it verified would spend this
/// application's reputation on their code.
/// </para>
/// </remarks>
public static class PluginTrust
{
    /// <summary>The digest of a file, lower-case hex.</summary>
    public static string Digest(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        using var stream = File.OpenRead(filePath);

        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Judges an extracted package by its entry assembly and the file it arrived in.
    /// </summary>
    /// <param name="packagePath">The <c>.plugin</c> file, which the digest is taken over.</param>
    /// <param name="entryAssemblyPath">The assembly the manifest names, which carries the signature.</param>
    /// <param name="publisher">
    /// This application's own signer subject. An unsigned application has none, in which case
    /// nothing can be verified and every package is unofficial — which is the honest answer for a
    /// build that cannot vouch for itself.
    /// </param>
    public static PluginTrustVerdict Judge(string packagePath, string entryAssemblyPath, string publisher)
    {
        var digest = Digest(packagePath);
        var signature = Authenticode.Verify(entryAssemblyPath);

        var verified = signature.Status == SignatureStatus.Valid
            && publisher.Length > 0
            && string.Equals(signature.Subject, publisher, StringComparison.Ordinal);

        return new PluginTrustVerdict(
            verified ? PluginTrustLevel.Verified : PluginTrustLevel.Unofficial,
            signature.Status,
            signature.Subject,
            digest);
    }

    /// <summary>
    /// This application's own signer subject, or empty when it is unsigned.
    /// </summary>
    public static string PublisherOf(string applicationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationPath);
        var signature = Authenticode.Verify(applicationPath);

        return signature.Status == SignatureStatus.Valid ? signature.Subject : string.Empty;
    }
}
