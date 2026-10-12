using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class BomService
    {
        private readonly ApplicationDbContext _db;
        private readonly CatalogMatcher _matcher;
        private readonly KiCadFieldBuilder _fields;
        private readonly ProjectAccessService _access;

        public BomService(
            ApplicationDbContext db,
            CatalogMatcher matcher,
            KiCadFieldBuilder fields,
            ProjectAccessService access)
        {
            _db = db;
            _matcher = matcher;
            _fields = fields;
            _access = access;
        }

        public async Task<List<object>> LinesAsync(int revisionId, string? group, CancellationToken cancellationToken)
        {
            bool grouped = !string.Equals(group, "none", StringComparison.OrdinalIgnoreCase);
            List<ProjectComponent> components = await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Variant == "")
                .OrderBy(c => c.Reference)
                .ThenBy(c => c.SymbolPath)
                .ToListAsync(cancellationToken);
            List<DescribedComponent> described = new();
            foreach (ProjectComponent component in components)
            {
                described.Add(await DescribeAsync(component, cancellationToken));
            }

            if (!grouped)
            {
                return described.Select(item => Line(new List<DescribedComponent> { item })).ToList();
            }

            List<object> lines = new();
            foreach (IGrouping<string, DescribedComponent> bucket in described.GroupBy(GroupKey))
            {
                lines.Add(Line(bucket.ToList()));
            }

            return lines;
        }

        public async Task<object> DiffAsync(ProjectRevision baseRevision, ProjectRevision targetRevision, CancellationToken cancellationToken)
        {
            List<ProjectComponent> baseRows = await LoadAsync(baseRevision.Id, cancellationToken);
            List<ProjectComponent> targetRows = await LoadAsync(targetRevision.Id, cancellationToken);
            Dictionary<string, ProjectComponent> targetByPath = targetRows.ToDictionary(c => c.SymbolPath, StringComparer.Ordinal);
            HashSet<string> basePaths = new(baseRows.Select(c => c.SymbolPath), StringComparer.Ordinal);

            List<object> changed = new();
            List<object> renamed = new();
            List<ProjectComponent> removed = new();
            foreach (ProjectComponent row in baseRows)
            {
                if (!targetByPath.TryGetValue(row.SymbolPath, out ProjectComponent? next))
                {
                    removed.Add(row);
                    continue;
                }

                Dictionary<string, string[]> fields = FieldChanges(row, next);
                bool referenceChanged = !KiCadFieldBuilder.Same(row.Reference, next.Reference);
                if (fields.Count == 0 && !referenceChanged)
                {
                    continue;
                }

                if (referenceChanged && fields.Count == 0)
                {
                    DescribedComponent described = await DescribeAsync(next, cancellationToken);
                    renamed.Add(new
                    {
                        reference = next.Reference,
                        value = next.Value ?? "",
                        status = described.Status,
                        partId = next.PartId,
                        from = new { reference = row.Reference, value = row.Value ?? "" },
                        to = new { reference = next.Reference, value = next.Value ?? "", status = described.Status },
                        changes = new Dictionary<string, string[]>
                        {
                            ["reference"] = new[] { row.Reference, next.Reference }
                        }
                    });
                    continue;
                }

                if (referenceChanged)
                {
                    fields["reference"] = new[] { row.Reference, next.Reference };
                }

                DescribedComponent targetDescribed = await DescribeAsync(next, cancellationToken);
                changed.Add(new
                {
                    reference = next.Reference,
                    value = next.Value ?? "",
                    status = targetDescribed.Status,
                    partId = next.PartId,
                    changes = fields
                });
            }

            List<ProjectComponent> added = targetRows.Where(row => !basePaths.Contains(row.SymbolPath)).ToList();
            List<object> replaced = new();
            HashSet<string> pairedAdded = new(StringComparer.Ordinal);
            List<ProjectComponent> stillRemoved = new();
            foreach (ProjectComponent row in removed)
            {
                ProjectComponent? match = added.FirstOrDefault(candidate =>
                    !pairedAdded.Contains(candidate.SymbolPath)
                    && !string.IsNullOrWhiteSpace(row.Reference)
                    && string.Equals(row.Reference, candidate.Reference, StringComparison.Ordinal));
                if (match == null)
                {
                    stillRemoved.Add(row);
                    continue;
                }

                pairedAdded.Add(match.SymbolPath);
                DescribedComponent to = await DescribeAsync(match, cancellationToken);
                replaced.Add(new
                {
                    reference = row.Reference,
                    from = new { value = row.Value ?? "" },
                    to = new { value = match.Value ?? "", status = to.Status }
                });
            }

            List<object> addedItems = new();
            foreach (ProjectComponent row in added.Where(row => !pairedAdded.Contains(row.SymbolPath)))
            {
                addedItems.Add(await SideItemAsync(row, cancellationToken));
            }

            List<object> removedItems = new();
            foreach (ProjectComponent row in stillRemoved)
            {
                removedItems.Add(await SideItemAsync(row, cancellationToken));
            }

            return new
            {
                @base = baseRevision.CommitSha,
                target = targetRevision.CommitSha,
                summary = new
                {
                    added = addedItems.Count,
                    removed = removedItems.Count,
                    replaced = replaced.Count,
                    changed = changed.Count,
                    renamed = renamed.Count
                },
                replaced,
                changed,
                added = addedItems,
                removed = removedItems,
                renamed
            };
        }

        public async Task<List<object>> UsageAsync(CallerContext caller, int partId, CancellationToken cancellationToken)
        {
            if (!caller.IsAuthenticated)
            {
                return new List<object>();
            }

            List<int> projectIds = await _access.Readable(caller).Select(p => p.Id).ToListAsync(cancellationToken);
            if (projectIds.Count == 0)
            {
                return new List<object>();
            }

            List<Project> projects = await _db.Projects
                .Where(p => projectIds.Contains(p.Id))
                .ToListAsync(cancellationToken);
            HashSet<int> revisionIds = projects.Where(p => p.HeadRevisionId != null).Select(p => p.HeadRevisionId!.Value).ToHashSet();
            List<ProjectRelease> releases = await _db.ProjectReleases
                .Where(r => projectIds.Contains(r.ProjectId) && r.State == "released")
                .ToListAsync(cancellationToken);
            foreach (ProjectRelease release in releases)
            {
                revisionIds.Add(release.RevisionId);
            }

            if (revisionIds.Count == 0)
            {
                return new List<object>();
            }

            List<ProjectRevision> revisions = await _db.ProjectRevisions
                .Where(r => revisionIds.Contains(r.Id))
                .ToListAsync(cancellationToken);
            Dictionary<int, ProjectRevision> revisionById = revisions.ToDictionary(r => r.Id);
            List<ProjectComponent> components = await _db.ProjectComponents
                .Where(c => c.PartId == partId && revisionIds.Contains(c.RevisionId) && c.Variant == "")
                .ToListAsync(cancellationToken);
            Dictionary<int, List<ProjectComponent>> componentsByRevision = components
                .GroupBy(c => c.RevisionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<object> rows = new();
            foreach (Project project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                int? countedRevisionId = project.HeadRevisionId != null
                    && componentsByRevision.ContainsKey(project.HeadRevisionId.Value)
                    ? project.HeadRevisionId
                    : releases
                        .Where(r => r.ProjectId == project.Id && componentsByRevision.ContainsKey(r.RevisionId))
                        .Select(r => r.RevisionId)
                        .Distinct()
                        .OrderByDescending(id => componentsByRevision[id].Count)
                        .ThenBy(id => id)
                        .Select(id => (int?)id)
                        .FirstOrDefault();

                if (countedRevisionId == null
                    || !revisionById.TryGetValue(countedRevisionId.Value, out ProjectRevision? revision))
                {
                    continue;
                }

                List<string> releaseNames = releases
                    .Where(r => r.ProjectId == project.Id && r.RevisionId == revision.Id)
                    .Select(r => r.Name)
                    .Distinct()
                    .ToList();
                rows.Add(new
                {
                    projectId = project.Id,
                    projectName = project.Name,
                    revision = revision.CommitSha,
                    release = releaseNames.Count == 0 ? "" : string.Join(", ", releaseNames),
                    quantity = componentsByRevision[revision.Id].Count
                });
            }

            return rows;
        }

        private async Task<List<ProjectComponent>> LoadAsync(int revisionId, CancellationToken cancellationToken)
        {
            return await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Variant == "")
                .OrderBy(c => c.SymbolPath)
                .ToListAsync(cancellationToken);
        }

        private async Task<object> SideItemAsync(ProjectComponent component, CancellationToken cancellationToken)
        {
            DescribedComponent described = await DescribeAsync(component, cancellationToken);
            return new
            {
                reference = component.Reference,
                value = component.Value ?? "",
                status = described.Status,
                partId = component.PartId
            };
        }

        private static Dictionary<string, string[]> FieldChanges(ProjectComponent left, ProjectComponent right)
        {
            Dictionary<string, string[]> changes = new(StringComparer.Ordinal);
            AddChange(changes, "value", left.Value, right.Value);
            AddChange(changes, "footprint", left.Footprint, right.Footprint);
            AddChange(changes, "mpn", left.Mpn, right.Mpn);
            AddChange(changes, "manufacturer", left.Manufacturer, right.Manufacturer);
            if (left.Dnp != right.Dnp)
            {
                changes["dnp"] = new[] { left.Dnp ? "true" : "false", right.Dnp ? "true" : "false" };
            }

            if (left.ExcludeFromBom != right.ExcludeFromBom)
            {
                changes["excludeFromBom"] = new[] { left.ExcludeFromBom ? "true" : "false", right.ExcludeFromBom ? "true" : "false" };
            }

            return changes;
        }

        private static void AddChange(Dictionary<string, string[]> changes, string name, string? left, string? right)
        {
            if (!KiCadFieldBuilder.Same(left, right))
            {
                changes[name] = new[] { left ?? "", right ?? "" };
            }
        }

        private static string GroupKey(DescribedComponent item)
        {
            ProjectComponent component = item.Component;
            if (component.PartId != null)
            {
                return "part:" + component.PartId.Value.ToString();
            }

            return "fields:" + (component.Value ?? "") + "\u001f" + (component.Footprint ?? "") + "\u001f" + (component.Mpn ?? "");
        }

        private static object Line(List<DescribedComponent> members)
        {
            DescribedComponent first = members[0];
            ProjectComponent component = first.Component;
            int quantity = members.Count(item => !item.Component.Dnp && !item.Component.ExcludeFromBom);
            List<string> references = members.Select(item => item.Component.Reference).Where(reference => reference.Length > 0).ToList();
            List<string> outdated = members.SelectMany(item => item.OutdatedFields).Distinct(StringComparer.Ordinal).ToList();
            string status = members.Select(item => item.Status).FirstOrDefault(value => value == "field_outdated")
                ?? members.Select(item => item.Status).FirstOrDefault(value => value == "eol")
                ?? members.Select(item => item.Status).FirstOrDefault(value => value == "ambiguous")
                ?? first.Status;
            return new
            {
                reference = string.Join(", ", references),
                references,
                value = component.Value ?? "",
                footprint = component.Footprint ?? "",
                mpn = component.Mpn ?? "",
                manufacturer = component.Manufacturer ?? "",
                quantity,
                status,
                dnp = members.All(item => item.Component.Dnp),
                excludeFromBom = members.All(item => item.Component.ExcludeFromBom),
                partId = component.PartId,
                partName = first.PartName ?? "",
                outdatedFields = outdated,
                symbolPath = members.Count == 1 ? component.SymbolPath : null
            };
        }

        private async Task<DescribedComponent> DescribeAsync(ProjectComponent component, CancellationToken cancellationToken)
        {
            if (component.PartId == null)
            {
                bool ambiguous = await _matcher.IsAmbiguousMpnAsync(component.Mpn, component.Manufacturer, cancellationToken);
                return new DescribedComponent(component, ambiguous ? "ambiguous" : "not_in_catalog", new List<string>(), null);
            }

            Part? part = await _db.Part.FirstOrDefaultAsync(p => p.Id == component.PartId, cancellationToken);
            if (part == null)
            {
                return new DescribedComponent(component, "not_in_catalog", new List<string>(), null);
            }

            KiCadHttpPart? library = part.Id == null ? null : await _fields.BuildForPartIdAsync(part.Id.Value, cancellationToken);
            List<string> outdated = library == null ? new List<string>() : KiCadFieldBuilder.DifferingFields(component, library);
            if (outdated.Count > 0)
            {
                return new DescribedComponent(component, "field_outdated", outdated, part.Name);
            }

            if (string.Equals(part.LifecycleStatus, "eol", StringComparison.OrdinalIgnoreCase))
            {
                return new DescribedComponent(component, "eol", outdated, part.Name);
            }

            return new DescribedComponent(component, "ok", outdated, part.Name);
        }

        private sealed class DescribedComponent
        {
            public DescribedComponent(ProjectComponent component, string status, List<string> outdatedFields, string? partName)
            {
                Component = component;
                Status = status;
                OutdatedFields = outdatedFields;
                PartName = partName;
            }

            public ProjectComponent Component { get; }

            public string Status { get; }

            public List<string> OutdatedFields { get; }

            public string? PartName { get; }
        }
    }
}
