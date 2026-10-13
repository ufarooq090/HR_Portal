using HrPortal.Domain.Files;
using HrPortal.Domain.Recruiting;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrPortal.Infrastructure.Recruiting;

/// <summary>
/// One-time Hangfire job (RB-04): extracts the text of CVs uploaded before CV search existed, so they become
/// searchable. Safe to re-run: it only picks up CVs whose extraction has never been attempted, and records the
/// attempt even when the format cannot be read (old .doc, scanned PDFs), so those are not retried. A CV whose file
/// cannot be read from storage is left for the next run.
/// </summary>
public sealed class CvTextBackfill(HrPortalDbContext db, IFileStore files, ICvTextExtractor extractor, TimeProvider clock, ILogger<CvTextBackfill> logger)
{
    public const int BatchSize = 50;

    /// <summary>Whether any CV still needs its text extracted; checked at startup to decide whether to enqueue the job.</summary>
    public Task<bool> IsNeededAsync(CancellationToken cancellationToken) =>
        db.CandidateCvs.AnyAsync(c => c.TextExtractedAtUtc == null, cancellationToken);

    /// <returns>How many CVs were processed.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await db.CandidateCvs
                .Where(c => c.TextExtractedAtUtc == null && (after == null || c.Id.CompareTo(after.Value) > 0))
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var cv in batch)
            {
                try
                {
                    await using var content = await files.OpenReadAsync(CandidateCv.Container, cv.StorageKey, cancellationToken);
                    cv.RecordText(extractor.Extract(content, cv.ContentType), clock.GetUtcNow());
                    processed++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Never log the CV's content; the id is enough to look into it.
                    logger.LogWarning(e, "Could not read CV {CvId} for text extraction; it will be retried on the next run", cv.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            after = batch[^1].Id;
        }

        logger.LogInformation("Extracted text from {Count} CVs", processed);
        return processed;
    }
}
