using SignedPdf.Models;

namespace SignedPdf.Services;

/// <summary>
/// Renders signature overlays (images, text, date stamps) onto an existing
/// PDF document. Implementations must be thread-safe; the service registers
/// renderers as singletons.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// Apply the supplied <paramref name="overlays"/> to <paramref name="basePdf"/>
    /// and return the resulting PDF as a new byte array. The input array is
    /// not mutated.
    /// </summary>
    /// <param name="basePdf">The source PDF document bytes.</param>
    /// <param name="overlays">
    /// Ordered list of overlay instructions. Overlays are applied in order;
    /// later overlays paint on top of earlier ones on the same page.
    /// </param>
    /// <returns>The rendered PDF bytes.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when an overlay targets a page outside the base PDF's page range
    /// or otherwise fails validation inside the renderer.
    /// </exception>
    byte[] RenderOverlays(byte[] basePdf, IReadOnlyList<SignatureOverlay> overlays);
}
