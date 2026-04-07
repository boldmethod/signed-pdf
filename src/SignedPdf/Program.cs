using System.Text.Json.Serialization;
using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SignedPdf.Configuration;
using SignedPdf.Models;
using SignedPdf.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var config = ServiceConfiguration.Load(builder.Configuration);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(RegionEndpoint.GetBySystemName(config.AwsRegion)));
builder.Services.AddSingleton<IS3Storage, S3Storage>();
builder.Services.AddSingleton<IPdfRenderer, ITextRenderer>();

var app = builder.Build();

// Expose the OpenAPI document in every environment. The repo is open source
// and the service has no sensitive surface — discoverability is a feature.
app.MapOpenApi();

app.MapHealthChecks("/health")
    .WithName("HealthCheck")
    .WithSummary("Liveness probe for container orchestrators.")
    .WithTags("System");

app.MapGet("/", () => Results.Ok(new { service = "signed-pdf", status = "running" }))
    .WithName("ServiceStatus")
    .WithSummary("Service banner.")
    .WithDescription("Returns the service name and a static running status. Useful for quick sanity checks.")
    .WithTags("System");

app.MapPost("/api/sign", SignPdfEndpoint.HandleAsync)
    .WithName("SignPdf")
    .WithSummary("Render signature overlays onto a PDF and return a presigned download URL.")
    .WithDescription(
        "Accepts a base64-encoded PDF and a list of overlay instructions (signature images, " +
        "text, date stamps). The service composites the overlays onto the document using iText, " +
        "uploads the result to AWS S3, and returns a short-lived presigned HTTPS URL the caller " +
        "can use to download the signed PDF. The service performs NO cryptographic signing — " +
        "all signature data is supplied externally.")
    .WithTags("Signing")
    .Accepts<SignPdfRequest>("application/json")
    .Produces<SignPdfResponse>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .ProducesProblem(StatusCodes.Status502BadGateway);

app.Run();

/// <summary>
/// Handler for <c>POST /api/sign</c>. Extracted into a dedicated type so the
/// XML documentation travels with the method and surfaces in the generated
/// OpenAPI document.
/// </summary>
internal static class SignPdfEndpoint
{
    /// <summary>
    /// Render signature overlays onto a PDF and return a presigned download URL.
    /// </summary>
    /// <remarks>
    /// Accepts a base64-encoded PDF and a list of overlay instructions
    /// (signature images, text, date stamps). The service composites the
    /// overlays onto the document using iText, uploads the result to AWS S3,
    /// and returns a short-lived presigned HTTPS URL the caller can use to
    /// download the signed PDF. The service performs NO cryptographic
    /// signing — all signature data is supplied externally.
    /// </remarks>
    /// <param name="request">The signing request body.</param>
    /// <param name="renderer">PDF renderer resolved from DI.</param>
    /// <param name="storage">S3 storage service resolved from DI.</param>
    /// <param name="logger">Request-scoped logger.</param>
    /// <param name="ct">Cancellation token for the HTTP request.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><c>200 OK</c> with a <see cref="SignPdfResponse"/> on success.</description></item>
    ///   <item><description><c>400 Bad Request</c> when validation fails or the base PDF is malformed.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> when PDF rendering fails unexpectedly.</description></item>
    ///   <item><description><c>502 Bad Gateway</c> when the upload to S3 fails.</description></item>
    /// </list>
    /// </returns>
    public static async Task<Results<Ok<SignPdfResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>> HandleAsync(
        SignPdfRequest request,
        IPdfRenderer renderer,
        IS3Storage storage,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BasePdfBase64))
            return TypedResults.BadRequest(new ErrorResponse("BasePdfBase64 is required."));

        if (request.Overlays is not { Count: > 0 })
            return TypedResults.BadRequest(new ErrorResponse("At least one overlay is required."));

        foreach (var overlay in request.Overlays)
        {
            if (overlay.PageNumber < 1)
                return TypedResults.BadRequest(new ErrorResponse("PageNumber must be >= 1."));
            if (overlay.Width <= 0 || overlay.Height <= 0)
                return TypedResults.BadRequest(new ErrorResponse("Width and Height must be > 0."));
            if (overlay.Type == OverlayType.SignatureImage && string.IsNullOrWhiteSpace(overlay.ImageBase64))
                return TypedResults.BadRequest(new ErrorResponse("ImageBase64 is required for SignatureImage overlays."));
            if (overlay.Type == OverlayType.Text && string.IsNullOrWhiteSpace(overlay.Text))
                return TypedResults.BadRequest(new ErrorResponse("Text is required for Text overlays."));
        }

        byte[] basePdf;
        try
        {
            basePdf = Convert.FromBase64String(request.BasePdfBase64);
        }
        catch (FormatException)
        {
            return TypedResults.BadRequest(new ErrorResponse("BasePdfBase64 is not valid base64."));
        }

        byte[] signedPdf;
        try
        {
            signedPdf = renderer.RenderOverlays(basePdf, request.Overlays);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF rendering failed");
            return TypedResults.Problem("Failed to process PDF.", statusCode: StatusCodes.Status500InternalServerError);
        }

        try
        {
            var result = await storage.UploadAndPresignAsync(signedPdf, ct);
            return TypedResults.Ok(new SignPdfResponse(result.DownloadUrl, result.ExpiresAtUtc));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "S3 upload failed");
            return TypedResults.Problem("Storage error.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

/// <summary>
/// Marker type for the top-level <see cref="Program"/> entry point. Required
/// so that <see cref="ILogger{TCategoryName}"/> injected into endpoint
/// handlers has a resolvable category name.
/// </summary>
public partial class Program;
