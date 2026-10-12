using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public static class ProjectExport
    {
        public static int ArchiveMaxMb()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("PROJECT_ARCHIVE_MAX_MB"), out int value) && value > 0)
            {
                return value;
            }

            return 200;
        }

        /// <summary>
        /// Directory passed to git archive. Null archives the whole commit.
        /// project_dir uses the directory of the .kicad_pro. repository archives everything.
        /// A project file at the repository root archives the whole commit.
        /// </summary>
        public static string? TreePath(Project project)
        {
            if (!string.Equals(project.ExportScope, "project_dir", StringComparison.Ordinal))
            {
                return null;
            }

            string file = (project.ProjectFile ?? "").Replace('\\', '/').Trim('/');
            int slash = file.LastIndexOf('/');
            if (slash <= 0)
            {
                return null;
            }

            return file[..slash];
        }

        public static string Sha256Hex(byte[] data)
        {
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        public static string? KicadVersion(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("files", out JsonElement files)
                    && files.ValueKind == JsonValueKind.Object
                    && TryProperty(files, "generatorVersion", out JsonElement generator)
                    && generator.ValueKind == JsonValueKind.String)
                {
                    string? version = generator.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return version;
                    }
                }

                if (TryProperty(root, "kicad", out JsonElement kicad) && kicad.ValueKind == JsonValueKind.String)
                {
                    return kicad.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// True when a 3D model entry in a git archive is still a Git LFS pointer.
        /// PDF and Gerber files are ignored; a missing LFS checkout must not fail those.
        /// </summary>
        public static bool ContainsLfsModelPointer(byte[] tar)
        {
            try
            {
                using MemoryStream stream = new(tar, writable: false);
                using TarReader reader = new(stream);
                while (reader.GetNextEntry() is TarEntry entry)
                {
                    if (entry.DataStream == null || !IsModelFile(entry.Name))
                    {
                        continue;
                    }

                    byte[] head = new byte[80];
                    int read = entry.DataStream.Read(head, 0, head.Length);
                    if (read <= 0)
                    {
                        continue;
                    }

                    string text = Encoding.ASCII.GetString(head, 0, read);
                    if (text.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static bool IsModelFile(string name)
        {
            string file = name.Replace('\\', '/');
            int slash = file.LastIndexOf('/');
            if (slash >= 0)
            {
                file = file[(slash + 1)..];
            }

            string lower = file.ToLowerInvariant();
            return lower.EndsWith(".step") || lower.EndsWith(".stp") || lower.EndsWith(".wrl")
                || lower.EndsWith(".wrz") || lower.EndsWith(".igs") || lower.EndsWith(".iges")
                || lower.EndsWith(".stl") || lower.EndsWith(".3mf");
        }

        public static bool TryProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }
    }
}
