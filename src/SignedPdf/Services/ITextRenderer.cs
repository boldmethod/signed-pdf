using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
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
        using var pdfDoc = new PdfDocument(reader, writer);

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
                    RenderText(pdfDoc, overlay, overlay.Text!);
                    break;

                case OverlayType.DateStamp:
                    var dateText = DateTime.UtcNow.ToString(overlay.Text ?? "MM/dd/yyyy");
                    RenderText(pdfDoc, overlay, dateText);
                    break;
            }

            canvas.Release();
        }

        pdfDoc.Close();
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

    private static void RenderText(PdfDocument pdfDoc, SignatureOverlay overlay, string text)
    {
        var fontFamily = overlay.FontFamily ?? StandardFonts.HELVETICA;
        var fontSize = (float)(overlay.FontSize ?? 12);
        var font = PdfFontFactory.CreateFont(fontFamily);

        using var document = new Document(pdfDoc);
        document.ShowTextAligned(
            new Paragraph(text).SetFont(font).SetFontSize(fontSize),
            (float)overlay.X,
            (float)overlay.Y,
            overlay.PageNumber,
            TextAlignment.LEFT,
            VerticalAlignment.BOTTOM,
            0);
    }
}
