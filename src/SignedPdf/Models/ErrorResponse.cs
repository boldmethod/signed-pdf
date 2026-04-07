namespace SignedPdf.Models;

/// <summary>
/// Standard error envelope returned by the signing endpoint when a request
/// fails validation or an upstream dependency errors. The shape is
/// intentionally minimal: a single human-readable <see cref="Error"/> field.
/// </summary>
/// <param name="Error">A human-readable description of what went wrong.</param>
public sealed record ErrorResponse(string Error);
