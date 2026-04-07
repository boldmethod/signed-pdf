using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SignedPdf.Configuration;
using SignedPdf.Models;

namespace SignedPdf.Middleware;

/// <summary>
/// Validates the <c>X-Service-Token</c> header against the configured
/// shared secret on every request to a protected path. Protected paths
/// are matched against <see cref="ProtectedPathPrefixes"/>; everything
/// else passes through unchanged so that <c>/health</c>, <c>/</c>, the
/// legacy <c>/api/sign</c> overlay endpoint, and the OpenAPI document
/// remain publicly accessible.
/// </summary>
/// <remarks>
/// Comparison is constant-time via
/// <see cref="CryptographicOperations.FixedTimeEquals"/> so timing
/// side-channels can't be used to brute-force the token character by
/// character. The token comes from <see cref="ServiceConfiguration"/>
/// which loads it from the <c>PDF_API_SERVICE_TOKEN</c> environment
/// variable (typically injected from AWS Secrets Manager via the ECS
/// task definition).
/// </remarks>
public sealed class ServiceTokenAuthMiddleware(RequestDelegate next, ServiceConfiguration config)
{
    /// <summary>
    /// HTTP header name carrying the shared secret.
    /// </summary>
    public const string HeaderName = "X-Service-Token";

    /// <summary>
    /// Path prefixes that require the <see cref="HeaderName"/> header.
    /// All other paths bypass the check.
    /// </summary>
    public static readonly string[] ProtectedPathPrefixes =
    [
        "/api/render-signed/"
    ];

    private static readonly byte[] EmptyToken = [];

    /// <summary>
    /// Middleware entry point. Allows the request through when its path
    /// is not protected, the header matches the configured token, or the
    /// service is starting up. Otherwise responds with
    /// <c>401 Unauthorized</c> and an <see cref="ErrorResponse"/> body.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var requiresAuth = false;
        foreach (var prefix in ProtectedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                requiresAuth = true;
                break;
            }
        }

        if (!requiresAuth)
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var headerValues)
            || headerValues.Count == 0
            || string.IsNullOrWhiteSpace(headerValues[0]))
        {
            await WriteUnauthorizedAsync(context, $"Missing {HeaderName} header.");
            return;
        }

        var presented = Encoding.UTF8.GetBytes(headerValues[0]!);
        var expected = Encoding.UTF8.GetBytes(config.ServiceToken);

        // FixedTimeEquals requires equal lengths to compare in constant
        // time. Compare against an empty buffer when lengths differ so
        // we still incur the same work and don't short-circuit on length.
        var match = presented.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(presented, expected);

        if (!match)
        {
            // Burn a constant-time compare against zero bytes so total
            // work is independent of the length mismatch above.
            CryptographicOperations.FixedTimeEquals(EmptyToken, EmptyToken);
            await WriteUnauthorizedAsync(context, $"Invalid {HeaderName}.");
            return;
        }

        await next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new ErrorResponse(message), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await context.Response.WriteAsync(body);
    }
}
