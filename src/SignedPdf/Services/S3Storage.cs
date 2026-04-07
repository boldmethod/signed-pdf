using Amazon.S3;
using Amazon.S3.Model;
using SignedPdf.Configuration;

namespace SignedPdf.Services;

/// <summary>
/// <see cref="IS3Storage"/> implementation backed by AWS S3. Uploads rendered
/// PDFs via <see cref="IAmazonS3.PutObjectAsync"/> under the bucket and key
/// prefix configured in <see cref="ServiceConfiguration"/>, and returns
/// time-limited presigned <c>GET</c> URLs for consumer retrieval.
/// </summary>
public sealed class S3Storage(IAmazonS3 s3Client, ServiceConfiguration config) : IS3Storage
{
    /// <inheritdoc />
    public async Task<UploadResult> UploadAndPresignAsync(byte[] pdfBytes, CancellationToken ct)
    {
        var key = $"{config.S3KeyPrefix}{Guid.NewGuid()}.pdf";

        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = config.S3Bucket,
            Key = key,
            InputStream = new MemoryStream(pdfBytes),
            ContentType = "application/pdf"
        }, ct);

        var expiresAtUtc = DateTime.UtcNow.Add(config.PresignedUrlTtl);
        var downloadUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = config.S3Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAtUtc
        });

        return new UploadResult(downloadUrl, expiresAtUtc);
    }
}
