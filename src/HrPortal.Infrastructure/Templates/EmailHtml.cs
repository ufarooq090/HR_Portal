using System.Text.RegularExpressions;
using Ganss.Xss;

namespace HrPortal.Infrastructure.Templates;

/// <summary>
/// Cleans email HTML from the editor (and HR's edits before sending) to a strict allowlist: text formatting, lists,
/// headings, links (http, https, mailto) and line breaks. No scripts, styles, event handlers, images or forms.
/// Merge fields ({{field}}) survive, including inside link addresses.
/// </summary>
public static partial class EmailHtml
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    public static string Sanitize(string html)
    {
        // Protect merge fields: braces in an href would otherwise be percent-encoded.
        var protectedHtml = Field().Replace(html, m => $"mergefield-{m.Groups[1].Value}-mergefield");
        var clean = Sanitizer.Sanitize(protectedHtml).Trim();
        return Marker().Replace(clean, m => $"{{{{{m.Groups[1].Value}}}}}");
    }

    /// <summary>Plain text of the HTML, to check a body is not empty.</summary>
    public static string Text(string html) => Tags().Replace(html, " ").Replace("&nbsp;", " ", StringComparison.Ordinal).Trim();

    private static HtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li", "a", "h1", "h2", "h3", "blockquote", "hr" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        return sanitizer;
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Field();

    [GeneratedRegex(@"mergefield-([A-Za-z0-9_]+)-mergefield")]
    private static partial Regex Marker();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
