using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Filesystem cache for revision renders and visual-diff layers.
    /// Evicts least-recently-used renders first, then least-recently-used diffs,
    /// until the tree is within PROJECT_CACHE_MAX_MB. A diff created in the current
    /// request can be protected so it is not removed immediately.
    /// </summary>
    public class ProjectCache
    {
        public const string AccessFileName = ".last-access";

        private readonly string _contentRoot;

        public ProjectCache(IWebHostEnvironment environment)
        {
            _contentRoot = environment.ContentRootPath;
        }

        public string Root => DataPaths.Resolve(_contentRoot, "PROJECT_CACHE_DIR", "Data", "ProjectCache");

        public static int MaxMegabytes()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("PROJECT_CACHE_MAX_MB"), out int value) && value > 0)
            {
                return value;
            }

            return 5120;
        }

        public string DiffRelative(Guid diffUuid) => "diff/" + diffUuid.ToString("D");

        public string RenderRelative(int projectId, string sha) => "render/" + projectId.ToString() + "/" + sha;

        public void Unpack(string relativeDirectory, byte[] zip, bool protect)
        {
            string destination = Absolute(relativeDirectory);
            Directory.CreateDirectory(destination);
            string rootFull = Path.GetFullPath(destination);
            using MemoryStream stream = new(zip);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                if (!TryCombine(rootFull, entry.FullName.Replace('\\', '/'), out string target))
                {
                    continue;
                }

                string? parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(target, overwrite: true);
            }

            NoteAccess(relativeDirectory, DateTime.UtcNow);
            Evict(protect ? relativeDirectory : null);
        }

        public byte[]? ReadFile(string relativeDirectory, string relativeFile)
        {
            if (!TryCombine(Path.GetFullPath(Absolute(relativeDirectory)), relativeFile, out string target) || !File.Exists(target))
            {
                return null;
            }

            NoteAccess(relativeDirectory, DateTime.UtcNow);
            return File.ReadAllBytes(target);
        }

        public bool Exists(string relativeDirectory, string relativeFile)
        {
            return TryCombine(Path.GetFullPath(Absolute(relativeDirectory)), relativeFile, out string target) && File.Exists(target);
        }

        public void NoteAccess(string relativeDirectory, DateTime utc)
        {
            string directory = Absolute(relativeDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, AccessFileName), utc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        public void Evict(string? protectedRelativeDirectory)
        {
            long maxBytes = MaxMegabytes() * 1024L * 1024L;
            List<CacheEntry> entries = ListEntries();
            long total = entries.Sum(entry => entry.Size);
            string? protect = Normalize(protectedRelativeDirectory);
            IEnumerable<CacheEntry> ordered = entries
                .Where(entry => protect == null || !string.Equals(entry.Relative, protect, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.IsDiff)
                .ThenBy(entry => entry.LastAccess)
                .ThenBy(entry => entry.Relative, StringComparer.OrdinalIgnoreCase);
            foreach (CacheEntry entry in ordered)
            {
                if (total <= maxBytes)
                {
                    break;
                }

                try
                {
                    Directory.Delete(entry.FullPath, recursive: true);
                    total -= entry.Size;
                }
                catch (Exception)
                {
                    // A file still in use is skipped. The next read tries again.
                }
            }
        }

        public long TotalBytes()
        {
            return ListEntries().Sum(entry => entry.Size);
        }

        public static bool IsSafeRelative(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/').Trim();
            if (normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            return !Path.IsPathRooted(path);
        }

        public string? PageFile(int projectId, string sha, string page)
        {
            string relative = RenderRelative(projectId, sha);
            string[] candidates =
            {
                "sch/p" + page + ".svg",
                "sch/" + page + ".svg",
                "pcb/" + page + ".svg",
                page.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? page : page + ".svg"
            };
            foreach (string candidate in candidates)
            {
                if (Exists(relative, candidate))
                {
                    return candidate;
                }
            }

            byte[]? manifest = ReadFile(relative, "manifest.json");
            if (manifest == null)
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(manifest);
                if (!doc.RootElement.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (JsonElement item in pages.EnumerateArray())
                {
                    string id = Text(item, "id");
                    string pageName = Text(item, "page");
                    string file = Text(item, "file");
                    if (file.Length == 0)
                    {
                        file = Text(item, "base");
                    }

                    if (file.Length == 0)
                    {
                        continue;
                    }

                    if (id == page || pageName == page || file == page)
                    {
                        return Exists(relative, file) ? file : null;
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private List<CacheEntry> ListEntries()
        {
            List<CacheEntry> entries = new();
            string root = Root;
            AddChildren(entries, Path.Combine(root, "diff"), "diff", isDiff: true, nested: false);
            string renderRoot = Path.Combine(root, "render");
            if (!Directory.Exists(renderRoot))
            {
                return entries;
            }

            foreach (string projectDir in Directory.EnumerateDirectories(renderRoot))
            {
                string projectName = Path.GetFileName(projectDir);
                foreach (string shaDir in Directory.EnumerateDirectories(projectDir))
                {
                    string relative = "render/" + projectName + "/" + Path.GetFileName(shaDir);
                    entries.Add(Describe(relative, shaDir, isDiff: false));
                }
            }

            return entries;
        }

        private static void AddChildren(List<CacheEntry> entries, string directory, string prefix, bool isDiff, bool nested)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (nested)
                {
                    AddChildren(entries, child, prefix + "/" + Path.GetFileName(child), isDiff, nested: false);
                }
                else
                {
                    entries.Add(Describe(prefix + "/" + Path.GetFileName(child), child, isDiff));
                }
            }
        }

        private static CacheEntry Describe(string relative, string fullPath, bool isDiff)
        {
            return new CacheEntry(Normalize(relative) ?? relative, fullPath, ReadAccess(fullPath), DirectorySize(fullPath), isDiff);
        }

        private static DateTime ReadAccess(string directory)
        {
            string path = Path.Combine(directory, AccessFileName);
            if (File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            {
                try
                {
                    return new DateTime(ticks, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Directory.GetLastWriteTimeUtc(directory);
                }
            }

            return Directory.GetLastWriteTimeUtc(directory);
        }

        private static long DirectorySize(string directory)
        {
            long size = 0;
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // Skip files that disappear while the size is measured.
                }
            }

            return size;
        }

        private string Absolute(string relativeDirectory)
        {
            string relative = (Normalize(relativeDirectory) ?? "").Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(Root, relative));
            string root = Path.GetFullPath(Root);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cache path escapes the cache directory.");
            }

            return full;
        }

        private static bool TryCombine(string rootFull, string relativeFile, out string target)
        {
            target = "";
            if (!IsSafeRelative(relativeFile))
            {
                return false;
            }

            string combined = Path.GetFullPath(Path.Combine(rootFull, relativeFile.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            target = combined;
            return true;
        }

        private static string? Normalize(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            return relative.Replace('\\', '/').Trim('/');
        }

        private static string Text(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                return "";
            }

            return value.GetString() ?? "";
        }

        private sealed record CacheEntry(string Relative, string FullPath, DateTime LastAccess, long Size, bool IsDiff);
    }
}
