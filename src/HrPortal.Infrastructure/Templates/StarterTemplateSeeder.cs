using HrPortal.Domain.Requisitions;
using HrPortal.Domain.Templates;
using HrPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Infrastructure.Templates;

/// <summary>
/// Adds any starter template (from the spec) that is not there yet, in every environment. A template HR have edited,
/// renamed or deactivated is never touched again; a name HR already use for their own template is skipped.
/// </summary>
public static class StarterTemplateSeeder
{
    public static async Task<int> SeedAsync(HrPortalDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var existingKeys = await db.EmailTemplates.Where(t => t.StarterKey != null).Select(t => t.StarterKey!).ToListAsync(ct);
        var existingNames = await db.EmailTemplates.Select(t => t.Name).ToListAsync(ct);
        var system = new Actor("system", "System", null);
        var added = 0;
        foreach (var starter in StarterTemplates.All.Where(s => !existingKeys.Contains(s.Key) && !existingNames.Contains(s.Content.Name)))
        {
            var content = starter.Content with { BodyHtml = EmailHtml.Sanitize(starter.Content.BodyHtml) };
            db.EmailTemplates.Add(EmailTemplate.Create(content, system, clock.GetUtcNow(), starter.Trigger, starter.Key));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }
}
