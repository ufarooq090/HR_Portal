namespace HrPortal.Domain.Templates;

/// <summary>Renders letters and documents to PDF.</summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken = default);
}
