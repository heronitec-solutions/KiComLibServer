using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Parts store every datasheet id in datasheet_ids and copy the first one to datasheet_id.
    /// </summary>
    public static class PartDatasheetUsage
    {
        public static List<int> Normalize(IEnumerable<int>? ids, int? primaryId)
        {
            List<int> normalized = (ids ?? Array.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (normalized.Count == 0 && primaryId is > 0)
            {
                normalized.Add(primaryId.Value);
            }

            return normalized;
        }

        public static async Task<List<Part>> FindPartsAsync(
            ApplicationDbContext db,
            int datasheetId,
            CancellationToken cancellationToken)
        {
            return await db.Part
                .FromSqlInterpolated($@"SELECT * FROM public.part WHERE datasheet_id = {datasheetId} OR COALESCE(datasheet_ids, '[]'::json)::jsonb @> ('[' || {datasheetId}::text || ']')::jsonb")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
    }
}
