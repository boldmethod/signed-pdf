using System.Text.Json.Serialization;
using Amazon;
using Amazon.S3;
using iText.Bouncycastle;
using iText.Bouncycastleconnector;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SignedPdf.Configuration;
using SignedPdf.Endpoints;
using SignedPdf.Middleware;
using SignedPdf.Models;
using SignedPdf.Services;

// iText 9 requires the BouncyCastle factory to be registered before any
// crypto operation. Doing it once here means every request through the
// PAdES renderer can rely on it being available.
BouncyCastleFactoryCreator.SetFactory(new BouncyCastleFactory());

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Bump request body cap to 50 MB. /api/render-signed/finalize carries
// preparedPdfBase64 which is ~33% larger than the binary PDF, so 50 MB
// supports binary PDFs up to ~35 MB comfortably.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50L * 1024L * 1024L;
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var config = ServiceConfiguration.Load(builder.Configuration);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(RegionEndpoint.GetBySystemName(config.AwsRegion)));
builder.Services.AddSingleton<IS3Storage, S3Storage>();
builder.Services.AddSingleton<IPdfRenderer, ITextRenderer>();

// PAdES rendering services. FontResources loads the bundled Liberation
// TTFs once at startup; the renderer composes everything per request.
builder.Services.AddSingleton<FontResources>();
builder.Services.AddSingleton<VisibleSignatureBlockRenderer>();
builder.Services.AddSingleton<PadesCmsBuilder>();
builder.Services.AddSingleton<IPadesRenderer, ITextPadesRenderer>();

var app = builder.Build();

// Service-token auth for /api/render-signed/* (registered before any
// endpoint mapping so it sees every protected request).
app.UseMiddleware<ServiceTokenAuthMiddleware>();

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

app.MapPost("/api/render-signed/prepare", RenderSignedEndpoint.PrepareAsync)
    .WithName("RenderSignedPrepare")
    .WithSummary("Prepare a PAdES-signed PDF/A-3 by reserving a signature placeholder and returning the digest to sign.")
    .WithDescription(
        "Step 1 of 2 in the stateless deferred-signing protocol. Renders the supplied HTML to PDF/A-3B, " +
        "embeds attachments as PDF/A-3 associated files, renders a visible signature block on an appended " +
        "last page, reserves a fixed-size signature placeholder via iText's PdfSigner, and returns the " +
        "prepared PDF (base64) plus the SHA-256 digest of the CMS signed attributes that the caller must " +
        "ECDSA-sign with their private key. The service holds NO state between this call and finalize.")
    .WithTags("Render Signed")
    .Accepts<PdfRenderPrepareRequest>("application/json")
    .Produces<PdfRenderPrepareResponse>(StatusCodes.Status200OK)
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapPost("/api/render-signed/finalize", RenderSignedEndpoint.FinalizeAsync)
    .WithName("RenderSignedFinalize")
    .WithSummary("Inject an externally-computed CMS signature into a prepared PDF and return the final PAdES-signed PDF/A-3.")
    .WithDescription(
        "Step 2 of 2 in the stateless deferred-signing protocol. Accepts the prepared PDF blob from " +
        "/prepare along with the externally-computed ECDSA signature bytes, the signer's X.509 certificate, " +
        "and an optional RFC 3161 timestamp token. Builds the CMS SignedData via iText's PdfPKCS7, injects " +
        "it into the reserved placeholder, and returns the final PAdES-B-B PDF/A-3 document as " +
        "application/pdf bytes for the caller to archive.")
    .WithTags("Render Signed")
    .Accepts<PdfRenderFinalizeRequest>("application/json")
    .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
    .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
    .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

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
