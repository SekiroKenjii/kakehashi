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

/// <summary>
/// Who signed this application, as far as a package is compared against it.
/// </summary>
/// <param name="Subject">The certificate subject, as the signature carries it.</param>
/// <param name="PublicKey">A digest of the signer's public key, lower-case hex.</param>
/// <remarks>
/// Both, because either alone is answerable by somebody else. A subject is a string any authority
/// the machine trusts can put on a certificate; a key with a different subject is a different
/// publisher who happens to hold the same key, which is not a thing that happens honestly.
/// <para>
/// The cost of pinning the key rather than the certificate: an application re-signed with a fresh
/// key pair stops verifying plugins signed with its predecessor, and they drop to unofficial until
/// they are re-signed too. A renewal over the same key is unaffected, which is the common case.
/// </para>
/// </remarks>
public sealed record PluginPublisher(string Subject, string PublicKey)
{
    /// <summary>An application that is not signed, and so can vouch for nothing.</summary>
    public static readonly PluginPublisher Nobody = new(string.Empty, string.Empty);

    /// <summary>Whether there is anything here to compare against. Fails closed on either half.</summary>
    public bool IsKnown => Subject.Length > 0 && PublicKey.Length > 0;
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
    /// This application's own signature. An unsigned application has none, in which case nothing
    /// can be verified and every package is unofficial — which is the honest answer for a build
    /// that cannot vouch for itself.
    /// </param>
    public static PluginTrustVerdict Judge(
        string packagePath, string entryAssemblyPath, PluginPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var digest = Digest(packagePath);
        var signature = Authenticode.Verify(entryAssemblyPath);

        // The key, not only the name: a subject is a string any authority the machine trusts can
        // put on a certificate, and the key is what makes a publisher the same publisher.
        var verified = signature.Status == SignatureStatus.Valid
            && publisher.IsKnown
            && string.Equals(signature.Subject, publisher.Subject, StringComparison.Ordinal)
            && string.Equals(signature.PublicKey, publisher.PublicKey, StringComparison.Ordinal);

        return new PluginTrustVerdict(
            verified ? PluginTrustLevel.Verified : PluginTrustLevel.Unofficial,
            signature.Status,
            signature.Subject,
            digest);
    }

    /// <summary>
    /// This application's own signer, or nobody when it is unsigned.
    /// </summary>
    public static PluginPublisher PublisherOf(string applicationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(applicationPath);
        var signature = Authenticode.Verify(applicationPath);

        return signature.Status == SignatureStatus.Valid
            ? new PluginPublisher(signature.Subject, signature.PublicKey)
            : PluginPublisher.Nobody;
    }
}
