namespace SignedPdf.Models;

public sealed record SignPdfRequest
{
    public required string BasePdfBase64 { get; init; }
    public required IReadOnlyList<SignatureOverlay> Overlays { get; init; }
}

public sealed record SignatureOverlay
{
    public required int PageNumber { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required OverlayType Type { get; init; }
    public string? Text { get; init; }
    public string? ImageBase64 { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
}

public enum OverlayType
{
    SignatureImage,
    Text,
    DateStamp
}
