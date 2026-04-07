using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using SignedPdf.Models;

namespace SignedPdf.Services;

public sealed class ITextRenderer : IPdfRenderer
{
    public byte[] RenderOverlays(byte[] basePdf, IReadOnlyList<SignatureOverlay> overlays)
    {
        using var inputStream = new MemoryStream(basePdf);
        using var outputStream = new MemoryStream();
        using var reader = new PdfReader(inputStream);
        using var writer = new PdfWriter(outputStream);
        var pdfDoc = new PdfDocument(reader, writer);

        try
        {
            foreach (var overlay in overlays)
            {
                if (overlay.PageNumber < 1 || overlay.PageNumber > pdfDoc.GetNumberOfPages())
                    throw new ArgumentException($"Page {overlay.PageNumber} is out of range (1-{pdfDoc.GetNumberOfPages()}).");

                var page = pdfDoc.GetPage(overlay.PageNumber);
                var canvas = new PdfCanvas(page);

                switch (overlay.Type)
                {
                    case OverlayType.SignatureImage:
                        RenderImage(canvas, overlay);
                        break;

                    case OverlayType.Text:
                        RenderText(canvas, overlay, overlay.Text!);
                        break;

                    case OverlayType.DateStamp:
                        var dateText = DateTime.UtcNow.ToString(overlay.Text ?? "MM/dd/yyyy");
                        RenderText(canvas, overlay, dateText);
                        break;
                }

                canvas.Release();
            }
        }
        finally
        {
            pdfDoc.Close();
        }

        return outputStream.ToArray();
    }

    private static void RenderImage(PdfCanvas canvas, SignatureOverlay overlay)
    {
        var imageBytes = Convert.FromBase64String(overlay.ImageBase64!);
        var imageData = ImageDataFactory.Create(imageBytes);

        canvas.AddImageFittedIntoRectangle(
            imageData,
            new iText.Kernel.Geom.Rectangle(
                (float)overlay.X,
                (float)overlay.Y,
                (float)overlay.Width,
                (float)overlay.Height),
            false);
    }

    private static void RenderText(PdfCanvas canvas, SignatureOverlay overlay, string text)
    {
        var fontFamily = overlay.FontFamily ?? StandardFonts.HELVETICA;
        var fontSize = (float)(overlay.FontSize ?? 12);
        var font = PdfFontFactory.CreateFont(fontFamily);

        canvas.BeginText()
            .SetFontAndSize(font, fontSize)
            .MoveText(overlay.X, overlay.Y)
            .ShowText(text)
            .EndText();
    }
}
