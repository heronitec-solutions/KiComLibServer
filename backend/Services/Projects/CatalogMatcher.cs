using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class CatalogMatch
    {
        public int? PartId { get; init; }

        public string Method { get; init; } = "none";

        public bool Ambiguous { get; init; }
    }

    /// <summary>
    /// First matching rule wins: KiComLibUUID, library name, then a unique MPN.
    /// Only part_id and match_method are stored. Ambiguous MPN stays match_method none.
    /// </summary>
    public class CatalogMatcher
    {
        private readonly ApplicationDbContext _db;
        private readonly AppSettingsService _settings;

        public CatalogMatcher(ApplicationDbContext db, AppSettingsService settings)
        {
            _db = db;
            _settings = settings;
        }

        public async Task ApplyAsync(IEnumerable<ProjectComponent> components, CancellationToken cancellationToken)
        {
            HashSet<string> nicknames = await NicknamesAsync(cancellationToken);
            foreach (ProjectComponent component in components)
            {
                CatalogMatch match = await MatchAsync(component, nicknames, cancellationToken);
                component.PartId = match.PartId;
                component.MatchMethod = match.Method;
            }
        }

        public async Task<CatalogMatch> MatchAsync(ProjectComponent component, CancellationToken cancellationToken)
        {
            HashSet<string> nicknames = await NicknamesAsync(cancellationToken);
            return await MatchAsync(component, nicknames, cancellationToken);
        }

        public async Task<bool> IsAmbiguousMpnAsync(string? mpn, string? manufacturer, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(mpn))
            {
                return false;
            }

            List<int> parts = await PartIdsForMpnAsync(mpn.Trim(), manufacturer, cancellationToken);
            return parts.Count > 1;
        }

        private async Task<CatalogMatch> MatchAsync(ProjectComponent component, HashSet<string> nicknames, CancellationToken cancellationToken)
        {
            if (component.KicomlibUuid != null)
            {
                int? byUuid = await _db.Part
                    .Where(p => p.Uuid == component.KicomlibUuid)
                    .OrderBy(p => p.Id)
                    .Select(p => p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (byUuid != null)
                {
                    return new CatalogMatch { PartId = byUuid, Method = "uuid" };
                }
            }

            (string? nickname, string? name) = SplitLibId(component.LibId);
            if (!string.IsNullOrWhiteSpace(nickname)
                && !string.IsNullOrWhiteSpace(name)
                && nicknames.Contains(nickname))
            {
                int? byName = await _db.Part
                    .Where(p => p.Name == name)
                    .OrderBy(p => p.Id)
                    .Select(p => p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (byName != null)
                {
                    return new CatalogMatch { PartId = byName, Method = "name" };
                }
            }

            if (!string.IsNullOrWhiteSpace(component.Mpn))
            {
                List<int> parts = await PartIdsForMpnAsync(component.Mpn.Trim(), component.Manufacturer, cancellationToken);
                if (parts.Count == 1)
                {
                    return new CatalogMatch { PartId = parts[0], Method = "mpn" };
                }

                if (parts.Count > 1)
                {
                    return new CatalogMatch { Method = "none", Ambiguous = true };
                }
            }

            return new CatalogMatch();
        }

        private async Task<List<int>> PartIdsForMpnAsync(string mpn, string? manufacturer, CancellationToken cancellationToken)
        {
            IQueryable<PartNumber> query = _db.PartNumber.Where(p => p.PartId != null && p.ManufacturerPartNumber == mpn);
            if (!string.IsNullOrWhiteSpace(manufacturer))
            {
                string wanted = manufacturer.Trim();
                query = query.Where(p => p.Manufacturer != null && p.Manufacturer.Name == wanted);
            }

            return await query
                .Select(p => p.PartId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<HashSet<string>> NicknamesAsync(CancellationToken cancellationToken)
        {
            string raw = await _settings.GetStringAsync(AppSettingsService.LibraryNicknamesKey, "KiComLib", cancellationToken);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length > 0)
                {
                    names.Add(part);
                }
            }

            if (names.Count == 0)
            {
                names.Add("KiComLib");
            }

            return names;
        }

        private static (string? Nickname, string? Name) SplitLibId(string? libId)
        {
            if (string.IsNullOrWhiteSpace(libId))
            {
                return (null, null);
            }

            int colon = libId.IndexOf(':');
            if (colon <= 0 || colon >= libId.Length - 1)
            {
                return (null, null);
            }

            return (libId[..colon].Trim(), libId[(colon + 1)..].Trim());
        }
    }
}
