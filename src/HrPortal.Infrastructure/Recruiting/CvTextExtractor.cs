using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HrPortal.Domain.Recruiting;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace HrPortal.Infrastructure.Recruiting;

/// <summary>
/// RB-04: text for full-text search, from PDF (PdfPig) and DOCX (Open XML SDK). Old binary .doc files and scanned
/// PDFs without a text layer yield nothing; they are still stored and downloadable, just not searchable.
/// </summary>
public sealed partial class CvTextExtractor(ILogger<CvTextExtractor> logger) : ICvTextExtractor
{
    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string? Extract(Stream content, string contentType)
    {
        try
        {
            var text = contentType switch
            {
                PdfContentType => FromPdf(content),
                DocxContentType => FromDocx(content),
                _ => null,
            };
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception e)
        {
            // A damaged file should not stop the upload; it just is not searchable.
            LogUnreadable(e, contentType);
            return null;
        }
    }

    private static string FromPdf(Stream content)
    {
        using var document = PdfDocument.Open(content);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            text.AppendJoin(' ', page.GetWords().Select(w => w.Text)).AppendLine();
            if (text.Length > CandidateCv.MaxTextLength)
            {
                break;
            }
        }

        return text.ToString();
    }

    private static string? FromDocx(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        return body is null ? null : string.Join('\n', body.Descendants<Paragraph>().Select(p => p.InnerText));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read text from a {ContentType} CV; it will not be searchable.")]
    private partial void LogUnreadable(Exception exception, string contentType);
}
