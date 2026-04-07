using SignedPdf.Models;

namespace SignedPdf.Services;

/// <summary>
/// Result of <see cref="IPadesRenderer.Prepare"/>: the prepared PDF
/// blob (with reserved signature placeholder) plus the digest the
/// caller must ECDSA-sign and the byte-range diagnostics.
/// </summary>
/// <param name="PreparedPdf">Bytes of the prepared PDF with placeholder.</param>
/// <param name="DigestToSign">SHA-256 of the DER-encoded CMS signed attributes.</param>
/// <param name="ByteRangeDigest">SHA-256 of the bytes covered by /ByteRange (informational).</param>
/// <param name="ByteRange">/ByteRange dictionary entry as a 4-tuple (offset1, length1, offset2, length2).</param>
/// <param name="SignatureFieldName">Field name used to reserve the placeholder.</param>
public sealed record PadesPrepareResult(
    byte[] PreparedPdf,
    byte[] DigestToSign,
    byte[] ByteRangeDigest,
    long[] ByteRange,
    string SignatureFieldName);

/// <summary>
/// Renders HTML to PDF/A-3B with embedded attachments and a visible
/// signature block, prepares the document for external PAdES signing,
/// and finalizes the document by injecting an externally-computed CMS
/// SignedData blob into the reserved placeholder.
/// </summary>
public interface IPadesRenderer
{
    /// <summary>
    /// Render the HTML, embed attachments, append the visible signature
    /// block on a new page, and reserve a fixed-size signature placeholder.
    /// Returns the prepared PDF and the digest the caller needs to ECDSA-sign.
    /// </summary>
    /// <exception cref="ArgumentException">Validation or rendering failure.</exception>
    PadesPrepareResult Prepare(PdfRenderPrepareRequest request);

    /// <summary>
    /// Inject the externally-computed CMS signature into the prepared PDF
    /// and return the final PAdES-signed PDF/A-3 bytes.
    /// </summary>
    /// <exception cref="ArgumentException">Validation failure or signature too large for reserved placeholder.</exception>
    byte[] Finalize(PdfRenderFinalizeRequest request);
}
