using System.IO.Compression;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class OutputsJobHandler : IProjectJobHandler
    {
        public static readonly string[] Kinds =
        {
            "schematic_pdf",
            "gerber_zip",
            "drill_zip",
            "pos_csv",
            "bom_csv",
            "erc_json",
            "drc_json",
            "step"
        };

        private readonly ApplicationDbContext _db;
        private readonly GitMirrorService _mirror;
        private readonly IKiCadRunnerClient _runner;
        private readonly ProjectBlobStore _blobs;
        private readonly IBlobStore _catalogBlobs;
        private readonly ILogger<OutputsJobHandler> _logger;

        public OutputsJobHandler(
            ApplicationDbContext db,
            GitMirrorService mirror,
            IKiCadRunnerClient runner,
            ProjectBlobStore blobs,
            IBlobStore catalogBlobs,
            ILogger<OutputsJobHandler> logger)
        {
            _db = db;
            _mirror = mirror;
            _runner = runner;
            _blobs = blobs;
            _catalogBlobs = catalogBlobs;
            _logger = logger;
        }

        public string Kind => "outputs";

        public string? CompletionNote { get; private set; }

        public async Task RunAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            (ProjectRelease release, ProjectRevision revision, Project project) = await LoadAsync(job, cancellationToken);
            string? lfsWarning = null;
            try
            {
                GitRepository repository = project.Repository ?? throw new InvalidOperationException("The project archive cannot be built because the git repository is missing.");
                (byte[] tar, lfsWarning) = await _mirror.ArchiveForOutputsAsync(
                    repository,
                    revision.CommitSha,
                    ProjectExport.TreePath(project),
                    ProjectExport.ArchiveMaxMb(),
                    cancellationToken);
                List<OutputBomLine> bom = await BomLinesAsync(revision.Id, cancellationToken);
                byte[]? models = await ModelsZipAsync(revision.Id, cancellationToken);
                byte[] zip = await _runner.OutputsAsync(
                    tar,
                    project.ProjectFile,
                    Kinds,
                    null,
                    bom.Count == 0 ? null : bom,
                    models,
                    cancellationToken);
                List<string> warnings = await StoreAsync(revision, zip, cancellationToken);
                CompletionNote = Join(lfsWarning, warnings);
                _logger.LogInformation("Stored fabrication outputs for release {ReleaseId}.", release.Id);
            }
            catch (RunnerCallException exception) when (!exception.Retryable)
            {
                CompletionNote = Join(lfsWarning, exception.Message);
            }
        }

        private async Task<(ProjectRelease Release, ProjectRevision Revision, Project Project)> LoadAsync(ProjectJob job, CancellationToken cancellationToken)
        {
            int? releaseId = ReadReleaseId(job.Payload);
            ProjectRelease? release = null;
            if (releaseId != null)
            {
                release = await _db.ProjectReleases.FirstOrDefaultAsync(r => r.Id == releaseId, cancellationToken);
            }

            if (release == null)
            {
                throw new InvalidOperationException("outputs job did not match a release.");
            }

            ProjectRevision? revision = await _db.ProjectRevisions
                .Include(r => r.Project)!.ThenInclude(p => p!.Repository)
                .FirstOrDefaultAsync(r => r.Id == release.RevisionId, cancellationToken);
            if (revision?.Project == null)
            {
                throw new InvalidOperationException("The release revision is not available, so the project archive cannot be built.");
            }

            return (release, revision, revision.Project);
        }

        internal static int? ReadReleaseId(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("releaseId", out JsonElement id) && id.TryGetInt32(out int parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private async Task<List<OutputBomLine>> BomLinesAsync(int revisionId, CancellationToken cancellationToken)
        {
            List<ProjectComponent> components = await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Variant == "" && !c.ExcludeFromBom && !c.Dnp)
                .ToListAsync(cancellationToken);
            return components
                .GroupBy(c => ((c.Value ?? "").Trim(), (c.Footprint ?? "").Trim(), (c.Mpn ?? "").Trim()))
                .Select(group => new OutputBomLine
                {
                    Reference = string.Join(", ", group.Select(c => c.Reference).OrderBy(r => r, StringComparer.OrdinalIgnoreCase)),
                    Value = group.Key.Item1,
                    Footprint = group.Key.Item2,
                    Mpn = group.Key.Item3,
                    Quantity = group.Count()
                })
                .OrderBy(line => line.Reference, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// KiComLib models zip as directory/filename, matching ${PATH_KEY_3DMODEL_DIR}/directory/filename in the board.
        /// </summary>
        private async Task<byte[]?> ModelsZipAsync(int revisionId, CancellationToken cancellationToken)
        {
            List<string?> footprints = await _db.ProjectComponents
                .Where(c => c.RevisionId == revisionId && c.Footprint != null && c.Footprint != "")
                .Select(c => c.Footprint)
                .Distinct()
                .ToListAsync(cancellationToken);
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string? footprint in footprints)
            {
                if (string.IsNullOrWhiteSpace(footprint))
                {
                    continue;
                }

                string name = footprint.Trim();
                int colon = name.LastIndexOf(':');
                if (colon >= 0 && colon < name.Length - 1)
                {
                    name = name[(colon + 1)..];
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            if (names.Count == 0)
            {
                return null;
            }

            List<string> nameList = names.ToList();
            var linked = await (
                from fp in _db.Footprint
                where fp.DeletedAt == null && fp.HeadRevisionId != null && nameList.Contains(fp.Name)
                join rev in _db.FootprintRevisions on fp.HeadRevisionId equals rev.Id
                join link in _db.FootprintRevisionModel3Ds on rev.Id equals link.FootprintRevisionId
                join model in _db.Model3D on link.Model3dId equals model.Id
                where model.DeletedAt == null && model.HeadRevisionId != null
                join mrev in _db.Model3DRevisions on model.HeadRevisionId equals mrev.Id
                where !mrev.IsDeleted && mrev.BlobHash != null
                select new { mrev.Directory, mrev.Filename, mrev.BlobHash }
            ).ToListAsync(cancellationToken);

            Dictionary<string, byte[]> entries = new(StringComparer.OrdinalIgnoreCase);
            foreach (var row in linked)
            {
                if (string.IsNullOrWhiteSpace(row.BlobHash) || string.IsNullOrWhiteSpace(row.Filename))
                {
                    continue;
                }

                string? entryName = ModelEntry(row.Directory, row.Filename);
                if (entryName == null || entries.ContainsKey(entryName))
                {
                    continue;
                }

                Blob? blob = await _catalogBlobs.GetAsync(row.BlobHash.Trim(), cancellationToken);
                if (blob?.Data == null || blob.Data.Length == 0)
                {
                    continue;
                }

                entries[entryName] = blob.Data;
            }

            if (entries.Count == 0)
            {
                return null;
            }

            using MemoryStream stream = new();
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (KeyValuePair<string, byte[]> entry in entries)
                {
                    ZipArchiveEntry zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                    using Stream output = zipEntry.Open();
                    output.Write(entry.Value);
                }
            }

            return stream.ToArray();
        }

        private async Task<List<string>> StoreAsync(ProjectRevision revision, byte[] zipBytes, CancellationToken cancellationToken)
        {
            using ZipArchive zip = new(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            ZipArchiveEntry? manifestEntry = zip.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                throw new InvalidOperationException("The runner archive has no manifest.json.");
            }

            using JsonDocument manifest = JsonDocument.Parse(ReadEntry(manifestEntry));
            string kicad = revision.KicadVersion ?? "unknown";
            if (manifest.RootElement.TryGetProperty("kicad", out JsonElement kicadElement) && kicadElement.ValueKind == JsonValueKind.String)
            {
                string? version = kicadElement.GetString();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    kicad = version.Trim();
                }
            }

            if (!manifest.RootElement.TryGetProperty("outputs", out JsonElement outputs) || outputs.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The runner manifest has no outputs list.");
            }

            foreach (JsonElement item in outputs.EnumerateArray())
            {
                if (!item.TryGetProperty("kind", out JsonElement kindElement) || kindElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("The runner manifest is missing an output kind.");
                }

                if (!item.TryGetProperty("fileName", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("The runner manifest is missing a file name.");
                }

                string kind = kindElement.GetString() ?? "";
                string fileName = SafeFileName(nameElement.GetString() ?? "");
                ZipArchiveEntry? file = zip.GetEntry(fileName) ?? zip.GetEntry(nameElement.GetString() ?? "");
                if (file == null)
                {
                    throw new InvalidOperationException("The runner archive is missing " + fileName + ".");
                }

                byte[] data = ReadEntry(file);
                string hash = await _blobs.PutAsync(data, MediaType(kind), cancellationToken);
                ProjectArtifact? existing = await _db.ProjectArtifacts.FirstOrDefaultAsync(
                    a => a.RevisionId == revision.Id && a.Kind == kind && a.Variant == "" && a.FileName == fileName,
                    cancellationToken);
                if (existing == null)
                {
                    _db.ProjectArtifacts.Add(new ProjectArtifact
                    {
                        RevisionId = revision.Id,
                        Kind = kind,
                        Variant = "",
                        FileName = fileName,
                        BlobHash = hash,
                        KicadVersion = kicad,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.BlobHash = hash;
                    existing.KicadVersion = kicad;
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            List<string> warnings = new();
            if (manifest.RootElement.TryGetProperty("warnings", out JsonElement warningElement) && warningElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement warning in warningElement.EnumerateArray())
                {
                    if (warning.ValueKind == JsonValueKind.String)
                    {
                        string? text = warning.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            warnings.Add(text.Trim());
                        }
                    }
                }
            }

            return warnings;
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        private static string SafeFileName(string name)
        {
            string file = name.Replace('\\', '/').Trim();
            int slash = file.LastIndexOf('/');
            if (slash >= 0)
            {
                file = file[(slash + 1)..];
            }

            if (string.IsNullOrWhiteSpace(file) || file.Contains("..", StringComparison.Ordinal) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("The runner manifest has an invalid file name.");
            }

            return file;
        }

        private static string? ModelEntry(string? directory, string filename)
        {
            string file = Path.GetFileName(filename.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(file) || file is "." or "..")
            {
                return null;
            }

            string dir = (directory ?? "").Replace('\\', '/').Trim('/');
            if (dir.Split('/').Any(part => part == ".." || part == "."))
            {
                return null;
            }

            return string.IsNullOrEmpty(dir) ? file : dir + "/" + file;
        }

        private static string MediaType(string kind)
        {
            return kind switch
            {
                "schematic_pdf" => "application/pdf",
                "gerber_zip" or "drill_zip" => "application/zip",
                "pos_csv" or "bom_csv" => "text/csv",
                "erc_json" or "drc_json" => "application/json",
                "step" => "model/step",
                _ => "application/octet-stream"
            };
        }

        private static string? Join(string? first, IReadOnlyList<string>? rest)
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(first))
            {
                parts.Add(first.Trim());
            }

            if (rest != null)
            {
                parts.AddRange(rest.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
            }

            if (parts.Count == 0)
            {
                return null;
            }

            string text = string.Join(" ", parts);
            return text.Length > 2000 ? text[..2000] : text;
        }

        private static string? Join(string? first, string? second)
        {
            return Join(first, string.IsNullOrWhiteSpace(second) ? null : new[] { second });
        }
    }
}
