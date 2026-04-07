namespace SignedPdf.Configuration;

/// <summary>
/// Runtime configuration for the signed-pdf service. Loaded once at startup
/// from <see cref="IConfiguration"/> (which in turn reads appsettings files
/// and environment variables) and registered as a singleton in DI.
/// </summary>
/// <param name="AwsRegion">
/// AWS region used for the S3 client (e.g. <c>us-west-2</c>). Read from the
/// <c>AWS_REGION</c> key.
/// </param>
/// <param name="S3Bucket">
/// Target S3 bucket where rendered PDFs are stored. Read from the
/// <c>S3_BUCKET</c> key.
/// </param>
/// <param name="S3KeyPrefix">
/// Key prefix applied to every uploaded object (e.g. <c>signed-pdfs/</c>).
/// Read from the <c>S3_KEY_PREFIX</c> key; defaults to <c>signed-pdfs/</c>.
/// </param>
/// <param name="PresignedUrlTtl">
/// Lifetime of presigned download URLs generated for consumers. Read from
/// the <c>PRESIGNED_URL_TTL_MINUTES</c> key (in minutes); defaults to 60.
/// </param>
public sealed record ServiceConfiguration(
    string AwsRegion,
    string S3Bucket,
    string S3KeyPrefix,
    TimeSpan PresignedUrlTtl)
{
    /// <summary>
    /// Build a <see cref="ServiceConfiguration"/> from the supplied
    /// <see cref="IConfiguration"/>, falling back to environment variables
    /// for any keys not present in the configuration.
    /// </summary>
    /// <param name="config">Application configuration (appsettings + env).</param>
    /// <returns>A fully-populated configuration record.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required key is missing or when
    /// <c>PRESIGNED_URL_TTL_MINUTES</c> is not a positive integer.
    /// </exception>
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
