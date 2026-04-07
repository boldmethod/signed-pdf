namespace SignedPdf.Services;

/// <summary>
/// The outcome of a successful S3 upload: a presigned download URL and the
/// UTC time at which it expires.
/// </summary>
/// <param name="DownloadUrl">
/// A presigned HTTPS <c>GET</c> URL for the uploaded object. The URL grants
/// read access to whoever possesses it, until <paramref name="ExpiresAtUtc"/>.
/// </param>
/// <param name="ExpiresAtUtc">UTC timestamp at which the URL stops being valid.</param>
public sealed record UploadResult(string DownloadUrl, DateTime ExpiresAtUtc);

/// <summary>
/// Persists rendered PDFs to object storage and returns time-limited
/// presigned URLs for consumers to retrieve them.
/// </summary>
public interface IS3Storage
{
    /// <summary>
    /// Upload the supplied PDF bytes under a newly-generated, unguessable key
    /// and return a presigned <c>GET</c> URL for the uploaded object.
    /// </summary>
    /// <param name="pdfBytes">The rendered PDF document to upload.</param>
    /// <param name="ct">Cancellation token used to abort the upload.</param>
    /// <returns>
    /// An <see cref="UploadResult"/> containing the presigned URL and its
    /// expiry time.
    /// </returns>
    Task<UploadResult> UploadAndPresignAsync(byte[] pdfBytes, CancellationToken ct);
}
