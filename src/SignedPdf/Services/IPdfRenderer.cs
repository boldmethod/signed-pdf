using SignedPdf.Models;

namespace SignedPdf.Services;

public interface IPdfRenderer
{
    byte[] RenderOverlays(byte[] basePdf, IReadOnlyList<SignatureOverlay> overlays);
}
