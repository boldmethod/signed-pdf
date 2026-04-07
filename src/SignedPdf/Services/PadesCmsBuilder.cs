using System.Text;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Crypto;
using iText.Signatures;

namespace SignedPdf.Services;

/// <summary>
/// Wraps iText's <see cref="PdfPKCS7"/> external-signature lifecycle for
/// the two-call deferred signing protocol used by
/// <c>/api/render-signed/prepare</c> and <c>/api/render-signed/finalize</c>.
/// </summary>
/// <remarks>
/// PdfPKCS7 has two roles in this codebase:
/// <list type="number">
///   <item><description>
///     During <em>prepare</em>, build the signed-attributes blob (which embeds
///     the byte-range hash as <c>messageDigest</c>) and return its SHA-256
///     so the caller can ECDSA-sign it offline.
///   </description></item>
///   <item><description>
///     During <em>finalize</em>, accept the externally-computed signature value,
///     attach it to a fresh PdfPKCS7 instance, optionally embed an RFC 3161
///     timestamp via <see cref="SuppliedTimestampClient"/>, and serialize the
///     completed CMS SignedData ready for injection into the prepared PDF's
///     reserved placeholder.
///   </description></item>
/// </list>
/// All members are stateless — a new builder instance is constructed per
/// request.
/// </remarks>
public sealed class PadesCmsBuilder
{
    /// <summary>
    /// SHA-256 algorithm name as expected by iText's
    /// <see cref="DigestAlgorithms"/> helpers.
    /// </summary>
    public const string Sha256 = DigestAlgorithms.SHA256;

    /// <summary>
    /// ECDSA algorithm identifier passed to
    /// <c>PdfPKCS7.SetExternalSignatureValue</c>.
    /// </summary>
    public const string Ecdsa = "ECDSA";

    /// <summary>
    /// Parse a single PEM-encoded X.509 certificate.
    /// </summary>
    /// <exception cref="ArgumentException">when the PEM is malformed.</exception>
    public IX509Certificate ParseCertificate(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new ArgumentException("Certificate PEM is required.", nameof(pem));

        try
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(pem));
            return BouncyCastleFactoryCreator.GetFactory().CreateX509Certificate(stream);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Certificate PEM is not a valid X.509 certificate.", nameof(pem), ex);
        }
    }

    /// <summary>
    /// Build the SHA-256 of the DER-encoded CMS signed attributes for the
    /// given byte-range digest. This is the value the external signer
    /// (KMS, HSM, etc.) is expected to ECDSA-sign.
    /// </summary>
    /// <param name="cert">Signer certificate.</param>
    /// <param name="byteRangeDigest">SHA-256 of the prepared PDF's <c>/ByteRange</c> bytes.</param>
    /// <returns>The SHA-256 of <c>DER(signedAttributes)</c>.</returns>
    public byte[] ComputeDigestToSign(IX509Certificate cert, byte[] byteRangeDigest)
    {
        var pkcs7 = new PdfPKCS7(null, [cert], Sha256, hasEncapContent: false);
        var attrBytes = pkcs7.GetAuthenticatedAttributeBytes(
            byteRangeDigest,
            PdfSigner.CryptoStandard.CADES,
            ocsp: null,
            crlBytes: null);

        // Hash the DER-encoded signed attributes ourselves so the caller
        // gets a fixed-size SHA-256 digest to feed to their ECDSA signer.
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(attrBytes);
    }

    /// <summary>
    /// Build the final CMS SignedData blob for injection into the prepared
    /// PDF's reserved placeholder.
    /// </summary>
    /// <param name="cert">Signer certificate (must match the one used in <see cref="ComputeDigestToSign"/>).</param>
    /// <param name="byteRangeDigest">SHA-256 of the prepared PDF's <c>/ByteRange</c> bytes (must match the value used in <see cref="ComputeDigestToSign"/>).</param>
    /// <param name="signatureBytes">ECDSA signature value over the digest returned by <see cref="ComputeDigestToSign"/>.</param>
    /// <param name="timestampToken">Optional RFC 3161 timestamp token for embedding as an unsigned signer-info attribute.</param>
    /// <returns>The complete CMS SignedData ready to be embedded in the PDF signature dictionary.</returns>
    public byte[] BuildCms(
        IX509Certificate cert,
        byte[] byteRangeDigest,
        byte[] signatureBytes,
        byte[]? timestampToken)
    {
        var pkcs7 = new PdfPKCS7(null, [cert], Sha256, hasEncapContent: false);
        pkcs7.SetExternalSignatureValue(signatureBytes, signedMessageContent: null, signatureAlgorithm: Ecdsa);

        ITSAClient? tsaClient = timestampToken is { Length: > 0 }
            ? new SuppliedTimestampClient(timestampToken)
            : null;

        return pkcs7.GetEncodedPKCS7(
            secondDigest: byteRangeDigest,
            sigtype: PdfSigner.CryptoStandard.CADES,
            tsaClient: tsaClient,
            ocsp: null,
            crlBytes: null);
    }
}
