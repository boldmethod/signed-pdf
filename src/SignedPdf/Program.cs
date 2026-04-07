using System.Text.Json.Serialization;
using Amazon;
using Amazon.S3;
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.MapGet("/", () => Results.Ok(new { service = "signed-pdf", status = "running" }));

app.MapPost("/api/sign", async (
    SignPdfRequest request,
    IPdfRenderer renderer,
    IS3Storage storage,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.BasePdfBase64))
        return Results.BadRequest(new { error = "BasePdfBase64 is required." });

    if (request.Overlays is not { Count: > 0 })
        return Results.BadRequest(new { error = "At least one overlay is required." });

    foreach (var overlay in request.Overlays)
    {
        if (overlay.PageNumber < 1)
            return Results.BadRequest(new { error = "PageNumber must be >= 1." });
        if (overlay.Width <= 0 || overlay.Height <= 0)
            return Results.BadRequest(new { error = "Width and Height must be > 0." });
        if (overlay.Type == OverlayType.SignatureImage && string.IsNullOrWhiteSpace(overlay.ImageBase64))
            return Results.BadRequest(new { error = "ImageBase64 is required for SignatureImage overlays." });
        if (overlay.Type == OverlayType.Text && string.IsNullOrWhiteSpace(overlay.Text))
            return Results.BadRequest(new { error = "Text is required for Text overlays." });
    }

    byte[] basePdf;
    try
    {
        basePdf = Convert.FromBase64String(request.BasePdfBase64);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "BasePdfBase64 is not valid base64." });
    }

    byte[] signedPdf;
    try
    {
        signedPdf = renderer.RenderOverlays(basePdf, request.Overlays);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PDF rendering failed");
        return Results.Problem("Failed to process PDF.", statusCode: 500);
    }

    try
    {
        var result = await storage.UploadAndPresignAsync(signedPdf, ct);
        return Results.Ok(new SignPdfResponse(result.DownloadUrl, result.ExpiresAtUtc));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "S3 upload failed");
        return Results.Problem("Storage error.", statusCode: 502);
    }
});

app.Run();
