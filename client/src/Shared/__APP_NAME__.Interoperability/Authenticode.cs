using System;
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
public sealed record FileSignature(SignatureStatus Status, string Subject, string Thumbprint)
{
    /// <summary>The answer for a file that carries no signature.</summary>
    public static readonly FileSignature Unsigned =
        new(SignatureStatus.Unsigned, string.Empty, string.Empty);
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
            ? new FileSignature(status, string.Empty, string.Empty)
            : new FileSignature(status, signer.Subject, signer.Thumbprint);
    }

    private static unsafe SignatureStatus VerifyTrust(string filePath)
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
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
            };
            data.Anonymous.pFile = &file;

            var result = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);

            data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
            _ = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);

            return Map(result);
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
