namespace SignedPdf.Models;

/// <summary>
/// Successful response from <c>POST /api/sign</c>. Contains a short-lived
/// presigned URL that the caller can use to download the completed PDF
/// directly from AWS S3.
/// </summary>
/// <param name="DownloadUrl">
/// A presigned HTTPS URL pointing to the rendered PDF in S3. This URL is
/// time-limited and grants <c>GET</c> access only. Anyone holding the URL
/// can download the document until it expires.
/// </param>
/// <param name="ExpiresAtUtc">
/// UTC timestamp at which <paramref name="DownloadUrl"/> stops being valid.
/// The TTL is controlled by the <c>PRESIGNED_URL_TTL_MINUTES</c> environment
/// variable and defaults to 60 minutes.
/// </param>
public sealed record SignPdfResponse(string DownloadUrl, DateTime ExpiresAtUtc);
