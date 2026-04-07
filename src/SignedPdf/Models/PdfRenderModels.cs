namespace SignedPdf.Models;

/// <summary>
/// Request body for the <c>POST /api/render-signed/prepare</c> endpoint.
/// Carries everything needed to render an HTML document into a PDF/A-3B
/// archive, embed associated files, render a visible signature block on
/// an appended last page, and reserve a fixed-size signature placeholder
/// suitable for offline ECDSA signing.
/// </summary>
/// <remarks>
/// This endpoint performs no cryptography. It only constructs the PDF
/// structure with a placeholder. The caller is expected to ECDSA-sign
/// the <c>digestToSignBase64</c> returned in the response, then call
/// <c>POST /api/render-signed/finalize</c> with the resulting signature
/// bytes plus the original prepared PDF blob.
/// </remarks>
public sealed record PdfRenderPrepareRequest
{
    /// <summary>
    /// HTML document body to render. The HTML is converted to PDF/A-3B
    /// using iText pdfHTML. Standard semantic HTML and inline CSS are
    /// supported. JavaScript and external network resources are ignored.
    /// </summary>
    public required string Html { get; init; }

    /// <summary>
    /// PEM-encoded X.509 certificate of the signer. Required at prepare
    /// time because the CMS signed-attributes structure (whose digest the
    /// caller signs) binds the certificate's issuer and serial number.
    /// The same certificate must be re-sent in the finalize request.
    /// </summary>
    public required string CertificatePem { get; init; }

    /// <summary>
    /// Files to embed in the PDF as PDF/A-3 associated files. Typically
    /// the canonical JSON payload that was cryptographically signed,
    /// allowing offline verification of the document's content against
    /// the embedded JSON.
    /// </summary>
    public required IReadOnlyList<PdfAttachment> Attachments { get; init; }

    /// <summary>
    /// Metadata for the visible signature block rendered on a new page
    /// appended to the document.
    /// </summary>
    public required PdfVisibleSignatureBlock VisibleSignatureBlock { get; init; }

    /// <summary>
    /// Optional name of the signature field in the resulting PDF.
    /// Defaults to <c>"Signature1"</c>.
    /// </summary>
    public string? SignatureFieldName { get; init; }

    /// <summary>
    /// Optional size in bytes of the reserved signature placeholder.
    /// Defaults to <c>16384</c>, which accommodates a P-256 ECDSA
    /// signature plus an X.509 certificate plus an RFC 3161 timestamp
    /// token plus DER overhead. Increase if finalize fails with
    /// "signature container too large".
    /// </summary>
    public int? SignatureFieldSize { get; init; }

    /// <summary>
    /// Optional hash algorithm identifier. Currently only <c>"SHA-256"</c>
    /// is supported. Defaults to <c>"SHA-256"</c>.
    /// </summary>
    public string? HashAlgorithm { get; init; }
}

/// <summary>
/// A file embedded in the PDF as a PDF/A-3 associated file. The file
/// content is base64-encoded so the entire signing payload can travel
/// over JSON without multipart bodies.
/// </summary>
/// <param name="Filename">Display name of the file as shown in PDF viewers.</param>
/// <param name="ContentType">MIME type, e.g. <c>application/json</c>.</param>
/// <param name="ContentBase64">Base64-encoded file contents.</param>
/// <param name="Relationship">
/// PDF/A-3 associated-file relationship. Defaults to
/// <see cref="AfRelationship.Source"/> when omitted.
/// </param>
public sealed record PdfAttachment(
    string Filename,
    string ContentType,
    string ContentBase64,
    AfRelationship? Relationship = null);

/// <summary>
/// PDF/A-3 associated-file relationship per ISO 19005-3, used as the
/// <c>/AFRelationship</c> entry on an embedded file specification.
/// </summary>
public enum AfRelationship
{
    /// <summary>The file is the source from which the PDF was derived.</summary>
    Source,
    /// <summary>The file is data referenced by the PDF.</summary>
    Data,
    /// <summary>The file is an alternative representation of PDF content.</summary>
    Alternative,
    /// <summary>The file supplements the PDF content.</summary>
    Supplement,
    /// <summary>No specified relationship.</summary>
    Unspecified
}

/// <summary>
/// Metadata describing the visible signature block rendered on a new
/// page appended to the document. All fields except
/// <see cref="CertificateNumber"/> are required.
/// </summary>
public sealed record PdfVisibleSignatureBlock
{
    /// <summary>Full legal name of the signer.</summary>
    /// <example>Jane Doe</example>
    public required string SignerName { get; init; }

    /// <summary>Role or title of the signer in the signing context.</summary>
    /// <example>Flight Instructor</example>
    public required string SignerRole { get; init; }

    /// <summary>
    /// Optional credential or certificate number identifying the signer
    /// (e.g., FAA airman certificate number).
    /// </summary>
    /// <example>CFI-1234567</example>
    public string? CertificateNumber { get; init; }

    /// <summary>
    /// Local date and time of the signing event with timezone offset.
    /// Rendered for human readability; the cryptographic timestamp
    /// (when present) is the authoritative time anchor.
    /// </summary>
    public required DateTimeOffset DateTime { get; init; }

    /// <summary>
    /// Free-form intent statement displayed to the signer at signing
    /// time and rendered into the visible block on the PDF.
    /// </summary>
    public required string IntentText { get; init; }

    /// <summary>
    /// Hex-encoded fingerprint of the signing public key. Rendered in
    /// monospaced type so a verifier can compare it character by character
    /// against an external trust store.
    /// </summary>
    public required string KeyFingerprint { get; init; }

    /// <summary>
    /// Free-form attestation paragraph describing how the document can be
    /// verified (e.g., "This record was electronically signed and can be
    /// verified in Adobe Acrobat Reader...").
    /// </summary>
    public required string AttestationText { get; init; }
}

/// <summary>
/// Successful response from <c>POST /api/render-signed/prepare</c>.
/// Contains the prepared PDF (with a reserved signature placeholder)
/// and the digest the caller must ECDSA-sign before calling finalize.
/// </summary>
public sealed record PdfRenderPrepareResponse
{
    /// <summary>
    /// The prepared PDF as a base64-encoded byte string. The caller must
    /// pass this back unchanged to <c>/api/render-signed/finalize</c>.
    /// </summary>
    public required string PreparedPdfBase64 { get; init; }

    /// <summary>
    /// Base64-encoded SHA-256 digest of the DER-encoded CMS signed
    /// attributes. The caller signs <em>this</em> with ECDSA, not the
    /// PDF byte range directly. The signed attributes contain the
    /// byte-range digest as their <c>messageDigest</c> attribute, so
    /// signing this value transitively binds the PDF content.
    /// </summary>
    public required string DigestToSignBase64 { get; init; }

    /// <summary>
    /// Echoed signature field name (defaults to <c>"Signature1"</c>).
    /// Pass it back to finalize to ensure the right field is filled.
    /// </summary>
    public required string SignatureFieldName { get; init; }

    /// <summary>Echoed hash algorithm identifier.</summary>
    public required string HashAlgorithm { get; init; }

    /// <summary>
    /// The four <c>/ByteRange</c> integers from the prepared PDF
    /// (<c>[offset1, length1, offset2, length2]</c>). Returned for
    /// diagnostic and verification purposes only — the caller does not
    /// need them for finalize.
    /// </summary>
    public required IReadOnlyList<long> ByteRange { get; init; }

    /// <summary>
    /// Base64-encoded SHA-256 digest of the bytes covered by
    /// <see cref="ByteRange"/>. Returned for diagnostic purposes only.
    /// This is NOT the value the caller signs (see
    /// <see cref="DigestToSignBase64"/>).
    /// </summary>
    public required string ByteRangeDigestBase64 { get; init; }
}

/// <summary>
/// Request body for the <c>POST /api/render-signed/finalize</c> endpoint.
/// Injects the externally-computed CMS signature into the prepared PDF's
/// reserved placeholder and returns the final PAdES-signed PDF/A-3 document.
/// </summary>
public sealed record PdfRenderFinalizeRequest
{
    /// <summary>
    /// The prepared PDF blob as returned by
    /// <c>POST /api/render-signed/prepare</c>. Must be passed back unchanged.
    /// </summary>
    public required string PreparedPdfBase64 { get; init; }

    /// <summary>
    /// Signature field name to fill (must match the value used during prepare).
    /// </summary>
    public required string SignatureFieldName { get; init; }

    /// <summary>
    /// PEM-encoded X.509 certificate of the signer. Must be the same
    /// certificate used during prepare. Re-sent here so the finalize
    /// endpoint is fully stateless.
    /// </summary>
    public required string CertificatePem { get; init; }

    /// <summary>
    /// Base64-encoded ECDSA signature over the
    /// <c>digestToSignBase64</c> returned by prepare.
    /// </summary>
    public required string SignatureBytes { get; init; }

    /// <summary>
    /// Optional base64-encoded RFC 3161 timestamp token computed over
    /// <c>SHA-256(signatureBytes)</c>. When present, the timestamp is
    /// embedded as an unsigned attribute in the CMS signer info,
    /// enabling Adobe Reader to display the trusted timestamp in the
    /// signature panel.
    /// </summary>
    public string? TimestampToken { get; init; }

    /// <summary>
    /// Signature algorithm identifier. Currently only
    /// <c>"ecdsa.p256.sha256"</c> is supported.
    /// </summary>
    public required string Algorithm { get; init; }
}
