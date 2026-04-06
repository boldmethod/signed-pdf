using Amazon.S3;
using Amazon.S3.Model;
using SignedPdf.Configuration;

namespace SignedPdf.Services;

public sealed class S3Storage(IAmazonS3 s3Client, ServiceConfiguration config) : IS3Storage
{
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
