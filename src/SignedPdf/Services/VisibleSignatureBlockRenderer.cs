using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using SignedPdf.Models;

namespace SignedPdf.Services;

/// <summary>
/// Renders the visible signature block on the appended last page of a
/// signed PDF. Pure iText layout API — no HTML, no CSS — so the output
/// is fully reproducible and PDF/A-3B compliant (DeviceGray colors only).
/// </summary>
/// <remarks>
/// Layout, in order, centered horizontally on a US Letter page with 1"
/// margins:
/// <list type="number">
///   <item><description>Title <c>"Electronic Signature"</c> in 24 pt bold sans</description></item>
///   <item><description>Horizontal rule</description></item>
///   <item><description>Two-column metadata block with signer name/role/cert vs date/time</description></item>
///   <item><description><c>"Intent of Signature"</c> heading + body paragraph</description></item>
///   <item><description><c>"Attestation"</c> heading + body paragraph</description></item>
///   <item><description><c>"Public Key Fingerprint"</c> heading + monospaced fingerprint</description></item>
///   <item><description>Italic footer line about Adobe Reader verification</description></item>
/// </list>
/// </remarks>
public sealed class VisibleSignatureBlockRenderer(FontResources fonts)
{
    private readonly FontResources _fonts = fonts;

    /// <summary>
    /// Append a fresh blank page to <paramref name="pdfDoc"/> and render
    /// the visible signature block on it.
    /// </summary>
    public void AppendAndRender(PdfDocument pdfDoc, PdfVisibleSignatureBlock block)
    {
        pdfDoc.AddNewPage(PageSize.LETTER);

        var pageNumber = pdfDoc.GetNumberOfPages();
        using var doc = new Document(pdfDoc, PageSize.LETTER);
        doc.SetMargins(72, 72, 72, 72); // 1 inch all around

        // Fresh per-request PdfFont instances bound to the active document.
        var requestFonts = _fonts.CreateFonts();
        var sans = requestFonts.Sans;
        var sansBold = requestFonts.SansBold;
        var sansItalic = requestFonts.SansItalic;
        var mono = requestFonts.Mono;

        // Default font on the Document so any layout pass that needs to
        // resolve a font (e.g. table column-width calculation) has one
        // available even before we explicitly set fonts on paragraphs.
        doc.SetFont(sans);

        // Title
        doc.Add(new Paragraph("Electronic Signature")
            .SetFont(sansBold)
            .SetFontSize(24)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetFontColor(ColorConstants.BLACK)
            .SetPageNumber(pageNumber)
            .SetMarginBottom(6));

        // Horizontal rule via a 1-row borderless table with a top border.
        var rule = new Table(UnitValue.CreatePercentArray([100f])).UseAllAvailableWidth();
        rule.AddCell(new Cell()
            .SetBorder(Border.NO_BORDER)
            .SetBorderTop(new SolidBorder(ColorConstants.BLACK, 1f))
            .SetHeight(1)
            .Add(new Paragraph(" ").SetMargin(0).SetFontSize(1)));
        doc.Add(rule);

        // Metadata: 2-column borderless table
        var meta = new Table(UnitValue.CreatePercentArray([60f, 40f])).UseAllAvailableWidth();
        meta.SetMarginTop(12).SetMarginBottom(12);

        // Left column: name, role, optional cert number
        var nameCell = new Cell().SetBorder(Border.NO_BORDER);
        nameCell.Add(new Paragraph(block.SignerName)
            .SetFont(sansBold)
            .SetFontSize(16)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        nameCell.Add(new Paragraph(block.SignerRole)
            .SetFont(sans)
            .SetFontSize(12)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        if (!string.IsNullOrWhiteSpace(block.CertificateNumber))
        {
            nameCell.Add(new Paragraph($"Certificate: {block.CertificateNumber}")
                .SetFont(sans)
                .SetFontSize(12)
                .SetFontColor(ColorConstants.BLACK));
        }
        meta.AddCell(nameCell);

        // Right column: date and time
        var dateCell = new Cell().SetBorder(Border.NO_BORDER);
        var localTime = block.DateTime;
        dateCell.Add(new Paragraph("Signed")
            .SetFont(sansBold)
            .SetFontSize(10)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        dateCell.Add(new Paragraph(localTime.ToString("MMMM d, yyyy"))
            .SetFont(sans)
            .SetFontSize(12)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        dateCell.Add(new Paragraph(localTime.ToString("HH:mm 'UTC'zzz"))
            .SetFont(sans)
            .SetFontSize(12)
            .SetFontColor(ColorConstants.BLACK));
        meta.AddCell(dateCell);

        doc.Add(meta);

        // Intent
        doc.Add(new Paragraph("Intent of Signature")
            .SetFont(sansBold)
            .SetFontSize(12)
            .SetMarginTop(12)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        doc.Add(new Paragraph(block.IntentText)
            .SetFont(sans)
            .SetFontSize(11)
            .SetMarginBottom(12)
            .SetFontColor(ColorConstants.BLACK));

        // Attestation
        doc.Add(new Paragraph("Attestation")
            .SetFont(sansBold)
            .SetFontSize(12)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        doc.Add(new Paragraph(block.AttestationText)
            .SetFont(sans)
            .SetFontSize(11)
            .SetMarginBottom(24)
            .SetFontColor(ColorConstants.BLACK));

        // Key fingerprint
        doc.Add(new Paragraph("Public Key Fingerprint")
            .SetFont(sansBold)
            .SetFontSize(10)
            .SetMarginBottom(2)
            .SetFontColor(ColorConstants.BLACK));
        doc.Add(new Paragraph(block.KeyFingerprint)
            .SetFont(mono)
            .SetFontSize(9)
            .SetMarginBottom(36)
            .SetFontColor(ColorConstants.BLACK));

        // Footer
        doc.Add(new Paragraph(
                "This document is signed with PAdES-B-B and can be verified offline " +
                "in Adobe Acrobat Reader or any PAdES-compatible viewer.")
            .SetFont(sansItalic)
            .SetFontSize(10)
            .SetTextAlignment(TextAlignment.CENTER)
            .SetFontColor(ColorConstants.BLACK));
    }
}

/// <summary>
/// Cached raw font program bytes for the four bundled Liberation faces.
/// PdfFont instances are document-scoped in iText, so we cache the bytes
/// at startup and let each request create fresh PdfFont objects via
/// <see cref="CreateFonts"/>.
/// </summary>
public sealed class FontResources
{
    /// <summary>Liberation Sans Regular TTF bytes.</summary>
    public byte[] SansBytes { get; }

    /// <summary>Liberation Sans Bold TTF bytes.</summary>
    public byte[] SansBoldBytes { get; }

    /// <summary>Liberation Sans Italic TTF bytes.</summary>
    public byte[] SansItalicBytes { get; }

    /// <summary>Liberation Mono Regular TTF bytes.</summary>
    public byte[] MonoBytes { get; }

    /// <summary>
    /// Load font files from <c>Resources/Fonts/</c> next to the running
    /// binary. Reads happen once at startup; subsequent requests reuse
    /// the byte arrays.
    /// </summary>
    public FontResources()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
        SansBytes = File.ReadAllBytes(System.IO.Path.Combine(dir, "LiberationSans-Regular.ttf"));
        SansBoldBytes = File.ReadAllBytes(System.IO.Path.Combine(dir, "LiberationSans-Bold.ttf"));
        SansItalicBytes = File.ReadAllBytes(System.IO.Path.Combine(dir, "LiberationSans-Italic.ttf"));
        MonoBytes = File.ReadAllBytes(System.IO.Path.Combine(dir, "LiberationMono-Regular.ttf"));
    }

    /// <summary>
    /// Create fresh <see cref="PdfFont"/> instances for the current request's
    /// <see cref="PdfDocument"/>. Each call loads the four TTF byte arrays
    /// into new <see cref="PdfFont"/> objects bound to whatever document
    /// they're first used in.
    /// </summary>
    public RequestFonts CreateFonts() => new(
        Sans: PdfFontFactory.CreateFont(SansBytes, iText.IO.Font.PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED),
        SansBold: PdfFontFactory.CreateFont(SansBoldBytes, iText.IO.Font.PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED),
        SansItalic: PdfFontFactory.CreateFont(SansItalicBytes, iText.IO.Font.PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED),
        Mono: PdfFontFactory.CreateFont(MonoBytes, iText.IO.Font.PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED));
}

/// <summary>
/// Per-request set of <see cref="PdfFont"/> instances bound to a single
/// <see cref="PdfDocument"/>. Returned by <see cref="FontResources.CreateFonts"/>.
/// </summary>
public sealed record RequestFonts(PdfFont Sans, PdfFont SansBold, PdfFont SansItalic, PdfFont Mono);
