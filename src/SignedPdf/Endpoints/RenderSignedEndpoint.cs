using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SignedPdf.Models;
using SignedPdf.Services;

namespace SignedPdf.Endpoints;

/// <summary>
/// Handlers for the two-call PAdES signing protocol exposed at
/// <c>/api/render-signed/prepare</c> and <c>/api/render-signed/finalize</c>.
/// </summary>
/// <remarks>
/// Both endpoints require the <c>X-Service-Token</c> header (validated
/// upstream by <see cref="Middleware.ServiceTokenAuthMiddleware"/>) and
/// are documented in detail in CLAUDE.md and the OpenAPI spec.
/// </remarks>
public static class RenderSignedEndpoint
{
    /// <summary>
    /// Render HTML to PDF/A-3B, embed attachments, render the visible
    /// signature block, reserve a signature placeholder, and return the
    /// digest the caller must ECDSA-sign.
    /// </summary>
    /// <param name="request">The prepare request body.</param>
    /// <param name="renderer">PAdES renderer resolved from DI.</param>
    /// <param name="logger">Request-scoped logger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><c>200 OK</c> with a <see cref="PdfRenderPrepareResponse"/> on success.</description></item>
    ///   <item><description><c>400 Bad Request</c> with an <see cref="ErrorResponse"/> on validation failure.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> on unexpected rendering failures.</description></item>
    /// </list>
    /// </returns>
    public static Task<Results<Ok<PdfRenderPrepareResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>> PrepareAsync(
        PdfRenderPrepareRequest request,
        IPadesRenderer renderer,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            var result = renderer.Prepare(request);
            var response = new PdfRenderPrepareResponse
            {
                PreparedPdfBase64 = Convert.ToBase64String(result.PreparedPdf),
                DigestToSignBase64 = Convert.ToBase64String(result.DigestToSign),
                SignatureFieldName = result.SignatureFieldName,
                HashAlgorithm = string.IsNullOrWhiteSpace(request.HashAlgorithm) ? "SHA-256" : request.HashAlgorithm!,
                ByteRange = result.ByteRange,
                ByteRangeDigestBase64 = Convert.ToBase64String(result.ByteRangeDigest)
            };
            return Task.FromResult<Results<Ok<PdfRenderPrepareResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.Ok(response));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult<Results<Ok<PdfRenderPrepareResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.BadRequest(new ErrorResponse(ex.Message)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "render-signed/prepare failed");
            return Task.FromResult<Results<Ok<PdfRenderPrepareResponse>, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.Problem("Failed to prepare signed PDF.", statusCode: StatusCodes.Status500InternalServerError));
        }
    }

    /// <summary>
    /// Inject the externally-computed CMS signature into the prepared PDF
    /// and return the final PAdES-signed PDF/A-3 bytes.
    /// </summary>
    /// <param name="request">The finalize request body.</param>
    /// <param name="renderer">PAdES renderer resolved from DI.</param>
    /// <param name="logger">Request-scoped logger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><c>200 OK</c> with <c>application/pdf</c> body on success.</description></item>
    ///   <item><description><c>400 Bad Request</c> on validation failure or oversized signature.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> on unexpected CMS injection failures.</description></item>
    /// </list>
    /// </returns>
    public static Task<Results<FileContentHttpResult, BadRequest<ErrorResponse>, ProblemHttpResult>> FinalizeAsync(
        PdfRenderFinalizeRequest request,
        IPadesRenderer renderer,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        try
        {
            var pdfBytes = renderer.Finalize(request);
            return Task.FromResult<Results<FileContentHttpResult, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.File(pdfBytes, "application/pdf", "signed.pdf"));
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult<Results<FileContentHttpResult, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.BadRequest(new ErrorResponse(ex.Message)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "render-signed/finalize failed");
            return Task.FromResult<Results<FileContentHttpResult, BadRequest<ErrorResponse>, ProblemHttpResult>>(
                TypedResults.Problem("Failed to finalize signed PDF.", statusCode: StatusCodes.Status500InternalServerError));
        }
    }
}
