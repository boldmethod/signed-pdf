using iText.Bouncycastleconnector;
using iText.Commons.Digest;
using iText.Signatures;

namespace SignedPdf.Services;

/// <summary>
/// An <see cref="ITSAClient"/> implementation that returns a pre-fetched
/// RFC 3161 timestamp token instead of contacting a TSA. Used by
/// <see cref="ITextPadesRenderer"/> so that the existing
/// <c>PdfPKCS7.GetEncodedPKCS7</c> overload can wrap a caller-supplied
/// timestamp into the CMS as an unsigned attribute on the signer info.
/// </summary>
/// <remarks>
/// The caller has already obtained the timestamp token from a real TSA
/// (the Signing module in TrainingApi handles primary/fallback selection
/// and signs <c>SHA-256(signatureBytes)</c> as the imprint). PdfApi never
/// touches the network during signing.
/// </remarks>
public sealed class SuppliedTimestampClient(byte[] timestampTokenBytes) : ITSAClient
{
    private readonly byte[] _timestampTokenBytes = timestampTokenBytes;

    /// <summary>
    /// Estimated size of the encoded timestamp token, in bytes. iText uses
    /// this when reserving space inside the CMS unsigned attributes.
    /// </summary>
    public int GetTokenSizeEstimate() => _timestampTokenBytes.Length;

    /// <summary>
    /// The message digest used to compute the imprint that the TSA signs
    /// over. SHA-256 is the only algorithm we support and matches the
    /// digest the caller used when requesting their token.
    /// </summary>
    public IMessageDigest GetMessageDigest() =>
        BouncyCastleFactoryCreator.GetFactory().CreateIDigest("SHA256");

    /// <summary>
    /// Returns the pre-fetched RFC 3161 timestamp token, ignoring the
    /// caller's <paramref name="imprint"/>. The imprint inside the token
    /// must already match what iText would compute, so the cryptographic
    /// chain (signed-attrs digest → signature value → timestamp imprint)
    /// is consistent.
    /// </summary>
    public byte[] GetTimeStampToken(byte[] imprint) => _timestampTokenBytes;
}
