using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Cryptography;
using Windows.Win32.Security.WinTrust;

namespace __ROOT_NAMESPACE__.Interoperability;

/// <summary>What verifying a file's Authenticode signature concluded.</summary>
public enum SignatureStatus
{
    /// <summary>The file carries no signature at all.</summary>
    Unsigned,

    /// <summary>Signed, unmodified since, and the chain builds to a trusted root.</summary>
    Valid,

    /// <summary>The file has changed since it was signed.</summary>
    Tampered,

    /// <summary>The chain does not reach a root this machine trusts.</summary>
    UntrustedRoot,

    /// <summary>A certificate in the chain has expired.</summary>
    Expired,

    /// <summary>A certificate in the chain was revoked.</summary>
    Revoked,

    /// <summary>An administrator or the user explicitly distrusted the publisher.</summary>
    Distrusted,

    /// <summary>The trust provider gave an answer this build does not map.</summary>
    Unknown,
}

/// <summary>A file's signature, and who signed it.</summary>
/// <param name="Status">What the trust provider concluded.</param>
/// <param name="Subject">The signer's certificate subject, empty when the file is unsigned.</param>
/// <param name="Thumbprint">The signer's certificate thumbprint, empty when the file is unsigned.</param>
/// <param name="PublicKey">
/// A SHA-256 over the signer's subject public key info, empty when the file is unsigned. It is what
/// identifies a publisher across a certificate renewal, which a subject name does not: a name is
/// only as unique as the set of authorities the machine trusts.
/// </param>
public sealed record FileSignature(
    SignatureStatus Status, string Subject, string Thumbprint, string PublicKey)
{
    /// <summary>The answer for a file that carries no signature.</summary>
    public static readonly FileSignature Unsigned =
        new(SignatureStatus.Unsigned, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Verifies a file's Authenticode signature through the trust provider Windows already uses.
/// </summary>
/// <remarks>
/// Building the certificate chain alone would answer a different question: it says the signer is
/// who they claim to be, not that the bytes are the ones they signed. Only the trust provider
/// compares the file's hash against the signature, which is the half that catches a modified
/// assembly.
/// </remarks>
public static class Authenticode
{
    private const int _trustEBadDigest = unchecked((int)0x80096010);
    private const int _trustENoSignature = unchecked((int)0x800B0100);
    private const int _trustESubjectNotTrusted = unchecked((int)0x800B0004);
    private const int _trustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int _certEExpired = unchecked((int)0x800B0101);
    private const int _certERevoked = unchecked((int)0x800B010C);
    private const int _certEUntrustedRoot = unchecked((int)0x800B0109);
    private const int _certEChaining = unchecked((int)0x800B010A);

    private const int _cryptENoRevocationCheck = unchecked((int)0x80092012);
    private const int _cryptERevocationOffline = unchecked((int)0x80092013);
    private const int _certERevocationFailure = unchecked((int)0x800B010E);

    public static FileSignature Verify(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var status = VerifyTrust(filePath);

        if (status == SignatureStatus.Unsigned)
        {
            return FileSignature.Unsigned;
        }

        using var signer = ReadSigner(filePath);

        return signer is null
            ? new FileSignature(status, string.Empty, string.Empty, string.Empty)
            : new FileSignature(status, signer.Subject, signer.Thumbprint, PublicKeyOf(signer));
    }

    /// <summary>A SHA-256 over the certificate's subject public key info, lower-case hex.</summary>
    /// <remarks>
    /// The key rather than the certificate, because a renewal issues a new certificate over the same
    /// key and a publisher that stayed the publisher should stay recognised.
    /// </remarks>
    private static string PublicKeyOf(X509Certificate2 certificate)
    {
        var info = certificate.PublicKey;
        var key = info.EncodedKeyValue.RawData;

        // The algorithm parameters are part of the key: an ECC key is a point and the curve it is
        // on. They are absent for RSA, where the algorithm alone settles how to read the modulus.
        var parameters = info.EncodedParameters?.RawData ?? [];
        var encoded = new byte[key.Length + parameters.Length];
        key.CopyTo(encoded, 0);
        parameters.CopyTo(encoded, key.Length);

        return Convert.ToHexStringLower(SHA256.HashData(encoded));
    }

    /// <summary>
    /// What the trust provider says about the file.
    /// </summary>
    /// <remarks>
    /// Twice, when the first answer is about revocation rather than about the signature. A cache
    /// that has never seen this chain's list cannot say whether it was revoked, and that is not the
    /// same as saying it was — so the question is asked again without it, and what comes back is
    /// the verdict on everything else. Fail closed on a revocation this machine knows about, fail
    /// open on one it cannot determine.
    /// </remarks>
    private static SignatureStatus VerifyTrust(string filePath)
    {
        var result = Verify(filePath, WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_WHOLECHAIN);

        if (result is _cryptENoRevocationCheck or _cryptERevocationOffline or _certERevocationFailure)
        {
            result = Verify(filePath, WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE);
        }

        return Map(result);
    }

    private static unsafe int Verify(string filePath, WINTRUST_DATA_REVOCATION_CHECKS revocation)
    {
        var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;

        fixed (char* path = filePath)
        {
            var file = new WINTRUST_FILE_INFO {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = path,
            };
            var data = new WINTRUST_DATA {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = revocation,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,

                // Cached lists only, never a CRL or an OCSP responder: an install must not need
                // the network, or stall on one that is not there.
                dwProvFlags = WINTRUST_DATA_PROVIDER_FLAGS.WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            data.Anonymous.pFile = &file;

            var result = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);

            data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
            _ = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);

            return result;
        }
    }

    private static SignatureStatus Map(int result)
    {
        return result switch {
            0 => SignatureStatus.Valid,
            _trustENoSignature => SignatureStatus.Unsigned,
            _trustEBadDigest => SignatureStatus.Tampered,
            _certEExpired => SignatureStatus.Expired,
            _certERevoked => SignatureStatus.Revoked,
            _trustEExplicitDistrust => SignatureStatus.Distrusted,
            _certEUntrustedRoot or _certEChaining or _trustESubjectNotTrusted =>
                SignatureStatus.UntrustedRoot,
            _ => SignatureStatus.Unknown,
        };
    }

    /// <summary>
    /// The certificate that signed the file, or null when the signature cannot be read back.
    /// </summary>
    /// <remarks>
    /// The managed shortcut for this is obsolete and its replacement loads certificate files
    /// rather than reading one out of a signed image, so the blob is fetched through the crypto
    /// API and decoded as the PKCS #7 message it is.
    /// </remarks>
    private static unsafe X509Certificate2? ReadSigner(string filePath)
    {
        void* message = null;
        HCERTSTORE store = default;

        try
        {
            fixed (char* path = filePath)
            {
                var queried = PInvoke.CryptQueryObject(
                    CERT_QUERY_OBJECT_TYPE.CERT_QUERY_OBJECT_FILE,
                    path,
                    CERT_QUERY_CONTENT_TYPE_FLAGS.CERT_QUERY_CONTENT_FLAG_PKCS7_SIGNED_EMBED,
                    CERT_QUERY_FORMAT_TYPE_FLAGS.CERT_QUERY_FORMAT_FLAG_BINARY,
                    0,
                    null,
                    null,
                    null,
                    &store,
                    &message,
                    null);

                if (!queried || message is null)
                {
                    return null;
                }
            }
            uint size = 0;

            if (!PInvoke.CryptMsgGetParam(message, PInvoke.CMSG_ENCODED_MESSAGE, 0, null, ref size)
                || size == 0)
            {
                return null;
            }
            var encoded = new byte[size];

            if (!PInvoke.CryptMsgGetParam(message, PInvoke.CMSG_ENCODED_MESSAGE, 0, encoded, ref size))
            {
                return null;
            }
            var signed = new SignedCms();
            signed.Decode(encoded);

            return signed.SignerInfos.Count == 0 ? null : signed.SignerInfos[0].Certificate;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        finally
        {
            if (message is not null)
            {
                _ = PInvoke.CryptMsgClose(message);
            }

            if (!store.IsNull)
            {
                _ = PInvoke.CertCloseStore(store, 0);
            }
        }
    }
}
