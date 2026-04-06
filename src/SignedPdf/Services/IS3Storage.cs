namespace SignedPdf.Services;

public sealed record UploadResult(string DownloadUrl, DateTime ExpiresAtUtc);

public interface IS3Storage
{
    Task<UploadResult> UploadAndPresignAsync(byte[] pdfBytes, CancellationToken ct);
}
