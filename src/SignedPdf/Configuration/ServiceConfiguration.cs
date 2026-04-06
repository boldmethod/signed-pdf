namespace SignedPdf.Configuration;

public sealed record ServiceConfiguration(
    string AwsRegion,
    string S3Bucket,
    string S3KeyPrefix,
    TimeSpan PresignedUrlTtl)
{
    public static ServiceConfiguration Load(IConfiguration config)
    {
        var region = config["AWS_REGION"]
            ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("AWS_REGION is not configured.");

        var bucket = config["S3_BUCKET"]
            ?? Environment.GetEnvironmentVariable("S3_BUCKET")
            ?? throw new InvalidOperationException("S3_BUCKET is not configured.");

        var prefix = config["S3_KEY_PREFIX"]
            ?? Environment.GetEnvironmentVariable("S3_KEY_PREFIX")
            ?? "signed-pdfs/";

        var ttlRaw = config["PRESIGNED_URL_TTL_MINUTES"]
            ?? Environment.GetEnvironmentVariable("PRESIGNED_URL_TTL_MINUTES")
            ?? "60";

        if (!int.TryParse(ttlRaw, out var ttlMinutes) || ttlMinutes <= 0)
            throw new InvalidOperationException("PRESIGNED_URL_TTL_MINUTES must be a positive integer.");

        return new ServiceConfiguration(region, bucket, prefix, TimeSpan.FromMinutes(ttlMinutes));
    }
}
