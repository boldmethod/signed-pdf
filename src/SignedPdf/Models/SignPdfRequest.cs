namespace SignedPdf.Models;

/// <summary>
/// Request body for the <c>POST /api/sign</c> endpoint. Contains a base PDF
/// and a collection of overlay instructions describing how signature elements
/// should be rendered onto the document.
/// </summary>
/// <remarks>
/// The service does not perform any cryptographic signing. It only composites
/// externally-generated e-signature data (images, text, timestamps) onto the
/// supplied PDF. All overlays use the standard PDF coordinate system
/// (bottom-left origin, Y increases upward, units in points — 1 point = 1/72 inch).
/// </remarks>
public sealed record SignPdfRequest
{
    /// <summary>
    /// The base PDF document to apply overlays to, encoded as a base64 string.
    /// </summary>
    /// <example>JVBERi0xLjcKJeLjz9MKMS...</example>
    public required string BasePdfBase64 { get; init; }

    /// <summary>
    /// One or more overlay instructions to render onto the PDF. Overlays are
    /// applied in the order supplied, which determines their z-order.
    /// </summary>
    public required IReadOnlyList<SignatureOverlay> Overlays { get; init; }
}

/// <summary>
/// A single overlay element to render at a specific location on a PDF page.
/// </summary>
/// <remarks>
/// Coordinates use the PDF coordinate system: <c>(0, 0)</c> is the bottom-left
/// corner of the page, Y increases upward, and all values are in points
/// (1 point = 1/72 inch). For <see cref="OverlayType.SignatureImage"/> overlays
/// the image is fitted into the rectangle defined by <see cref="X"/>,
/// <see cref="Y"/>, <see cref="Width"/>, and <see cref="Height"/>. For
/// <see cref="OverlayType.Text"/> and <see cref="OverlayType.DateStamp"/>
/// overlays, <see cref="X"/> and <see cref="Y"/> are the baseline of the first
/// glyph and <see cref="Width"/>/<see cref="Height"/> are currently reserved
/// but still required.
/// </remarks>
public sealed record SignatureOverlay
{
    /// <summary>
    /// The 1-based page number in the base PDF to apply this overlay to.
    /// </summary>
    /// <example>1</example>
    public required int PageNumber { get; init; }

    /// <summary>
    /// Horizontal position in PDF points, measured from the left edge of the page.
    /// </summary>
    /// <example>150</example>
    public required double X { get; init; }

    /// <summary>
    /// Vertical position in PDF points, measured from the bottom edge of the page.
    /// </summary>
    /// <example>660</example>
    public required double Y { get; init; }

    /// <summary>
    /// Width of the overlay bounding box in PDF points. Must be greater than zero.
    /// </summary>
    /// <example>120</example>
    public required double Width { get; init; }

    /// <summary>
    /// Height of the overlay bounding box in PDF points. Must be greater than zero.
    /// </summary>
    /// <example>30</example>
    public required double Height { get; init; }

    /// <summary>
    /// The kind of overlay to render. Determines which of
    /// <see cref="Text"/> or <see cref="ImageBase64"/> is required.
    /// </summary>
    public required OverlayType Type { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is <see cref="OverlayType.Text"/>. For
    /// <see cref="OverlayType.DateStamp"/> this is an optional custom
    /// <see cref="DateTime"/> format string (defaults to <c>MM/dd/yyyy</c>).
    /// Ignored for <see cref="OverlayType.SignatureImage"/>.
    /// </summary>
    /// <example>John Doe</example>
    public string? Text { get; init; }

    /// <summary>
    /// Required when <see cref="Type"/> is <see cref="OverlayType.SignatureImage"/>.
    /// A base64-encoded PNG or JPEG image. Ignored for other overlay types.
    /// </summary>
    public string? ImageBase64 { get; init; }

    /// <summary>
    /// Optional font family to use for text overlays. Defaults to
    /// <c>Helvetica</c>. Only standard PDF font names are guaranteed to work
    /// across environments (<c>Helvetica</c>, <c>Times-Roman</c>, <c>Courier</c>,
    /// and their bold/italic variants).
    /// </summary>
    /// <example>Helvetica</example>
    public string? FontFamily { get; init; }

    /// <summary>
    /// Optional font size in PDF points. Defaults to <c>12</c>.
    /// </summary>
    /// <example>12</example>
    public double? FontSize { get; init; }
}

/// <summary>
/// The type of overlay to render onto a PDF page.
/// </summary>
public enum OverlayType
{
    /// <summary>
    /// Render a base64-encoded image (PNG or JPEG) fitted into the overlay
    /// rectangle. Used for e-signature artwork.
    /// </summary>
    SignatureImage,

    /// <summary>
    /// Render a string of literal text at the overlay's baseline position.
    /// </summary>
    Text,

    /// <summary>
    /// Render the current UTC date as text, using the overlay's
    /// <see cref="SignatureOverlay.Text"/> as an optional format string.
    /// </summary>
    DateStamp
}
