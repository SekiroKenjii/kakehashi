using System;
using System.IO;
using __ROOT_NAMESPACE__.Interoperability;
using Xunit;

namespace __ROOT_NAMESPACE__.App.Tests.Interoperability;

/// <summary>
/// Unit tests for <see cref="Authenticode"/>, against files every Windows machine has.
/// </summary>
/// <remarks>
/// A signed system binary is the only fixture that does not need a certificate to be generated and
/// trusted first, and the tampered case is the one that matters: building the certificate chain
/// alone would call a modified file valid, because the signer really is who they claim to be.
/// </remarks>
public sealed class AuthenticodeTests : IDisposable
{
    private static readonly string _signed = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    [Fact]
    public void Verify_SignedSystemBinary_IsValidAndNamesItsSigner()
    {
        var signature = Authenticode.Verify(_signed);

        Assert.Equal(SignatureStatus.Valid, signature.Status);
        Assert.Contains("Microsoft", signature.Subject, StringComparison.Ordinal);
        Assert.NotEmpty(signature.Thumbprint);
    }

    [Fact]
    public void Verify_UnsignedAssembly_IsUnsignedAndNamesNobody()
    {
        // This test assembly is the unsigned image every run is guaranteed to have.
        var path = typeof(AuthenticodeTests).Assembly.Location;

        var signature = Authenticode.Verify(path);

        Assert.Equal(SignatureStatus.Unsigned, signature.Status);
        Assert.Equal(string.Empty, signature.Subject);
        Assert.Equal(string.Empty, signature.Thumbprint);
    }

    /// <summary>
    /// A file the trust provider cannot read as an image at all answers differently from one that
    /// simply carries no signature, and the only thing the caller may conclude from either is that
    /// it is not <see cref="SignatureStatus.Valid"/>.
    /// </summary>
    [Fact]
    public void Verify_SomethingThatIsNotAnImage_IsNotValid()
    {
        var path = Write("not-an-image.dll", "not a signed image");

        var signature = Authenticode.Verify(path);

        Assert.NotEqual(SignatureStatus.Valid, signature.Status);
    }

    [Fact]
    public void Verify_SignedBinaryWithAByteChanged_IsTampered()
    {
        Directory.CreateDirectory(_scratch);
        var path = Path.Combine(_scratch, "tampered.dll");
        File.Copy(_signed, path);

        using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
        {
            stream.Seek(0x400, SeekOrigin.Begin);
            stream.WriteByte(0x42);
        }

        var signature = Authenticode.Verify(path);

        Assert.Equal(SignatureStatus.Tampered, signature.Status);
    }

    [Fact]
    public void Verify_NoPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => Authenticode.Verify(string.Empty));
    }

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_scratch);
        var path = Path.Combine(_scratch, name);
        File.WriteAllText(path, content);

        return path;
    }
}
