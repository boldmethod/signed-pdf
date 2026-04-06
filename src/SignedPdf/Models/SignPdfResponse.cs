namespace SignedPdf.Models;

public sealed record SignPdfResponse(string DownloadUrl, DateTime ExpiresAtUtc);
