using System.Security.Cryptography;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Filespec;
using iText.Layout.Font;
using iText.Pdfa;
using iText.Signatures;
using SignedPdf.Models;

namespace SignedPdf.Services;

/// <summary>
/// iText 9 / pdfHTML 6 implementation of the two-call PAdES rendering
/// protocol. Composes HTML conversion, attachment embedding, visible
/// signature block rendering, signature placeholder reservation, and
/// CMS signature injection into a single cohesive renderer.
/// </summary>
public sealed class ITextPadesRenderer : IPadesRenderer
{
    private const string DefaultSignatureFieldName = "Signature1";
    private const int DefaultSignatureFieldSize = 16384;
    private const string OutputIntentRegistryName = "http://www.color.org";
    private const string OutputIntentInfo = "sRGB IEC61966-2.1";

    private readonly VisibleSignatureBlockRenderer _blockRenderer;
    private readonly PadesCmsBuilder _cmsBuilder;
    private readonly string _iccProfilePath;
    private readonly string _fontsDirectory;

    /// <summary>
    /// Construct a renderer with the supplied dependencies. Resource
    /// paths are resolved relative to <see cref="AppContext.BaseDirectory"/>
    /// so the bundled fonts and ICC profile are picked up automatically.
    /// </summary>
    public ITextPadesRenderer(VisibleSignatureBlockRenderer blockRenderer, PadesCmsBuilder cmsBuilder)
    {
        _blockRenderer = blockRenderer;
        _cmsBuilder = cmsBuilder;
        _iccProfilePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "Profiles", "sRGB2014.icc");
        _fontsDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
    }

    /// <inheritdoc />
    public PadesPrepareResult Prepare(PdfRenderPrepareRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Html))
            throw new ArgumentException("html is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CertificatePem))
            throw new ArgumentException("certificatePem is required.", nameof(request));
        if (request.Attachments is null)
            throw new ArgumentException("attachments is required (may be empty).", nameof(request));
        if (request.VisibleSignatureBlock is null)
            throw new ArgumentException("visibleSignatureBlock is required.", nameof(request));

        var fieldName = string.IsNullOrWhiteSpace(request.SignatureFieldName)
            ? DefaultSignatureFieldName
            : request.SignatureFieldName!;
        var fieldSize = request.SignatureFieldSize ?? DefaultSignatureFieldSize;
        if (fieldSize <= 0)
            throw new ArgumentException("signatureFieldSize must be positive.", nameof(request));

        // Step 1: Build the PDF/A-3B body via HtmlConverter. The layout
        // Document and the underlying PdfADocument both close as part of
        // ConvertToPdf, producing a complete PDF/A-3B in bodyStream.
        byte[] bodyPdfBytes;
        using (var bodyStream = new MemoryStream())
        {
            var bodyWriter = new PdfWriter(bodyStream);
            using var iccStream = File.OpenRead(_iccProfilePath);
            var outputIntent = new PdfOutputIntent("Custom", "", OutputIntentRegistryName, OutputIntentInfo, iccStream);
            using var bodyDoc = new PdfADocument(bodyWriter, PdfAConformance.PDF_A_3B, outputIntent);

            var converterProperties = new ConverterProperties();
            var fontProvider = new FontProvider();
            foreach (var ttf in Directory.EnumerateFiles(_fontsDirectory, "*.ttf"))
            {
                fontProvider.AddFont(ttf);
            }
            converterProperties.SetFontProvider(fontProvider);

            HtmlConverter.ConvertToPdf(request.Html, bodyDoc, converterProperties);
            // bodyDoc is closed here by the using block.
            bodyPdfBytes = bodyStream.ToArray();
        }

        // Step 2: re-open the body in stamping mode to add associated files
        // and the visible signature block. PdfADocument re-opened from a
        // PdfReader knows the existing conformance and output intent.
        byte[] unsignedPdfBytes;
        using (var unsignedStream = new MemoryStream())
        {
            var stampReader = new PdfReader(new MemoryStream(bodyPdfBytes));
            var stampWriter = new PdfWriter(unsignedStream);
            var stampDoc = new PdfADocument(stampReader, stampWriter, new StampingProperties());

            foreach (var attachment in request.Attachments)
            {
                EmbedAttachment(stampDoc, attachment);
            }

            _blockRenderer.AppendAndRender(stampDoc, request.VisibleSignatureBlock);

            // Explicit Close() flushes the visible-block Canvas content into
            // unsignedStream BEFORE we read the byte array. A `using`
            // declaration would defer this until end-of-block, AFTER ToArray.
            stampDoc.Close();
            unsignedPdfBytes = unsignedStream.ToArray();
        }

        // Step 4: reserve the signature placeholder via PdfSigner. The captured
        // byte-range digest is the SHA-256 of the bytes that the eventual CMS
        // signature will cover.
        byte[] preparedPdfBytes;
        var captureContainer = new EmptyExternalSignatureContainer();
        using (var preparedStream = new MemoryStream())
        {
            var reader = new PdfReader(new MemoryStream(unsignedPdfBytes));
            var signer = new PdfSigner(reader, preparedStream, new StampingProperties().UseAppendMode());
            signer.GetSignerProperties().SetFieldName(fieldName);
            signer.SignExternalContainer(captureContainer, fieldSize);
            preparedPdfBytes = preparedStream.ToArray();
        }

        if (captureContainer.ByteRangeDigest is null)
            throw new InvalidOperationException("PdfSigner did not invoke the external signature container.");

        // Step 5: compute the digest the caller must ECDSA-sign.
        var cert = _cmsBuilder.ParseCertificate(request.CertificatePem);
        var digestToSign = _cmsBuilder.ComputeDigestToSign(cert, captureContainer.ByteRangeDigest);

        // Step 6: extract /ByteRange for diagnostic return.
        var byteRange = ExtractByteRange(preparedPdfBytes, fieldName);

        return new PadesPrepareResult(
            preparedPdfBytes,
            digestToSign,
            captureContainer.ByteRangeDigest,
            byteRange,
            fieldName);
    }

    /// <inheritdoc />
    public byte[] Finalize(PdfRenderFinalizeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PreparedPdfBase64))
            throw new ArgumentException("preparedPdfBase64 is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SignatureFieldName))
            throw new ArgumentException("signatureFieldName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CertificatePem))
            throw new ArgumentException("certificatePem is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SignatureBytes))
            throw new ArgumentException("signatureBytes is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Algorithm))
            throw new ArgumentException("algorithm is required.", nameof(request));

        byte[] preparedPdfBytes;
        try { preparedPdfBytes = Convert.FromBase64String(request.PreparedPdfBase64); }
        catch (FormatException) { throw new ArgumentException("preparedPdfBase64 is not valid base64.", nameof(request)); }

        byte[] signatureBytes;
        try { signatureBytes = Convert.FromBase64String(request.SignatureBytes); }
        catch (FormatException) { throw new ArgumentException("signatureBytes is not valid base64.", nameof(request)); }

        byte[]? timestampToken = null;
        if (!string.IsNullOrWhiteSpace(request.TimestampToken))
        {
            try { timestampToken = Convert.FromBase64String(request.TimestampToken); }
            catch (FormatException) { throw new ArgumentException("timestampToken is not valid base64.", nameof(request)); }
        }

        var cert = _cmsBuilder.ParseCertificate(request.CertificatePem);

        // Re-derive the byte-range digest from the prepared PDF by parsing
        // /ByteRange directly and hashing the covered bytes — no second
        // PdfSigner pass needed.
        var byteRangeDigest = ComputeByteRangeDigest(preparedPdfBytes, request.SignatureFieldName);
        var cmsBytes = _cmsBuilder.BuildCms(cert, byteRangeDigest, signatureBytes, timestampToken);

        // Inject the CMS into the reserved placeholder via SignDeferred.
        var prebuiltContainer = new PrebuiltSignatureContainer(cmsBytes);
        using var finalOutput = new MemoryStream();
        try
        {
            using var reader = new PdfReader(new MemoryStream(preparedPdfBytes));
            PdfSigner.SignDeferred(reader, request.SignatureFieldName, finalOutput, prebuiltContainer);
        }
        catch (iText.Kernel.Exceptions.PdfException ex)
        {
            throw new ArgumentException(
                "Signature container too large for reserved field. Increase signatureFieldSize on prepare. " + ex.Message,
                nameof(request),
                ex);
        }

        return finalOutput.ToArray();
    }

    private static void EmbedAttachment(PdfDocument pdfDoc, PdfAttachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.Filename))
            throw new ArgumentException("attachment filename is required.");
        if (string.IsNullOrWhiteSpace(attachment.ContentType))
            throw new ArgumentException("attachment contentType is required.");
        if (string.IsNullOrWhiteSpace(attachment.ContentBase64))
            throw new ArgumentException("attachment contentBase64 is required.");

        byte[] contentBytes;
        try { contentBytes = Convert.FromBase64String(attachment.ContentBase64); }
        catch (FormatException) { throw new ArgumentException($"attachment '{attachment.Filename}' contentBase64 is not valid base64."); }

        var afName = MapAfRelationship(attachment.Relationship ?? AfRelationship.Source);
        var mimeType = new PdfName(attachment.ContentType);

        var spec = PdfFileSpec.CreateEmbeddedFileSpec(
            pdfDoc,
            contentBytes,
            attachment.Filename,
            attachment.Filename,
            mimeType,
            null,
            afName);

        pdfDoc.AddAssociatedFile(attachment.Filename, spec);
    }

    private static PdfName MapAfRelationship(AfRelationship rel) => rel switch
    {
        AfRelationship.Source => PdfName.Source,
        AfRelationship.Data => PdfName.Data,
        AfRelationship.Alternative => PdfName.Alternative,
        AfRelationship.Supplement => PdfName.Supplement,
        AfRelationship.Unspecified => PdfName.Unspecified,
        _ => PdfName.Source
    };

    private static long[] ExtractByteRange(byte[] preparedPdfBytes, string fieldName)
    {
        using var reader = new PdfReader(new MemoryStream(preparedPdfBytes));
        using var doc = new PdfDocument(reader);
        var sigUtil = new SignatureUtil(doc);
        var sigDict = sigUtil.GetSignatureDictionary(fieldName)
            ?? throw new InvalidOperationException($"Signature field '{fieldName}' not found in prepared PDF.");
        var byteRangeArr = sigDict.GetAsArray(PdfName.ByteRange)
            ?? throw new InvalidOperationException("Signature dictionary has no /ByteRange entry.");
        var result = new long[4];
        for (var i = 0; i < 4; i++)
        {
            result[i] = byteRangeArr.GetAsNumber(i).LongValue();
        }
        return result;
    }

    private static byte[] ComputeByteRangeDigest(byte[] preparedPdfBytes, string fieldName)
    {
        var byteRange = ExtractByteRange(preparedPdfBytes, fieldName);
        using var sha = SHA256.Create();
        sha.TransformBlock(preparedPdfBytes, (int)byteRange[0], (int)byteRange[1], null, 0);
        sha.TransformFinalBlock(preparedPdfBytes, (int)byteRange[2], (int)byteRange[3]);
        return sha.Hash!;
    }

    /// <summary>
    /// External signature container used during the prepare phase.
    /// Returns an empty byte array (so iText keeps the reserved
    /// placeholder zeroed) and captures the byte-range hash of whatever
    /// data iText feeds it, so the prepare endpoint can return that
    /// hash to the caller.
    /// </summary>
    private sealed class EmptyExternalSignatureContainer : IExternalSignatureContainer
    {
        public byte[]? ByteRangeDigest { get; private set; }

        public byte[] Sign(Stream data)
        {
            using var sha = SHA256.Create();
            ByteRangeDigest = sha.ComputeHash(data);
            return [];
        }

        public void ModifySigningDictionary(PdfDictionary signatureDict)
        {
            signatureDict.Put(PdfName.Filter, PdfName.Adobe_PPKLite);
            signatureDict.Put(PdfName.SubFilter, PdfName.ETSI_CAdES_DETACHED);
        }
    }

    /// <summary>
    /// External signature container used during the finalize phase.
    /// Returns the pre-built CMS bytes and leaves the signing dictionary
    /// alone (it was already populated during prepare).
    /// </summary>
    private sealed class PrebuiltSignatureContainer(byte[] cmsBytes) : IExternalSignatureContainer
    {
        private readonly byte[] _cmsBytes = cmsBytes;

        public byte[] Sign(Stream data) => _cmsBytes;

        public void ModifySigningDictionary(PdfDictionary signatureDict)
        {
            // No modification — the dictionary was set up during prepare.
        }
    }
}
