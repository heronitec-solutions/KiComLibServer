using System.Text.Json;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public static class AnalysisDocument
    {
        public static List<ProjectComponent> ReadComponents(string json, int revisionId)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!ProjectExport.TryProperty(doc.RootElement, "components", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return new List<ProjectComponent>();
            }

            List<ProjectComponent> components = new();
            HashSet<string> paths = new(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                ProjectComponent component = ReadComponent(item, revisionId, index);
                if (paths.Add(component.SymbolPath))
                {
                    components.Add(component);
                }

                index++;
            }

            return components;
        }

        public static int WarningCount(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return 0;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (ProjectExport.TryProperty(doc.RootElement, "warnings", out JsonElement warnings) && warnings.ValueKind == JsonValueKind.Array)
                {
                    return warnings.GetArrayLength();
                }
            }
            catch (JsonException)
            {
                return 0;
            }

            return 0;
        }

        public static JsonElement? Section(string? json, string name)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!ProjectExport.TryProperty(doc.RootElement, name, out JsonElement section) || section.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return null;
                }

                return section.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static ProjectComponent ReadComponent(JsonElement item, int revisionId, int index)
        {
            string? symbolPath = StringValue(item, "symbolPath");
            if (string.IsNullOrWhiteSpace(symbolPath))
            {
                symbolPath = "unknown/" + index.ToString();
            }

            string fieldsJson = "{}";
            string? mpn = null;
            string? manufacturer = null;
            Guid? kicomlibUuid = null;
            if (ProjectExport.TryProperty(item, "fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object)
            {
                fieldsJson = fields.GetRawText();
                mpn = Field(fields, "MPN");
                manufacturer = Field(fields, "Manufacturer");
                string? uuidText = Field(fields, "KiComLibUUID");
                if (Guid.TryParse(uuidText, out Guid parsed))
                {
                    kicomlibUuid = parsed;
                }
            }

            return new ProjectComponent
            {
                RevisionId = revisionId,
                Variant = "",
                SymbolPath = symbolPath,
                SymbolUuids = Uuids(item),
                SheetNames = StringValue(item, "sheetNames") ?? "/",
                Reference = StringValue(item, "reference") ?? "",
                Value = StringValue(item, "value"),
                Footprint = StringValue(item, "footprint"),
                LibId = StringValue(item, "libId"),
                KicomlibUuid = kicomlibUuid,
                Mpn = string.IsNullOrWhiteSpace(mpn) ? null : mpn,
                Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer,
                Fields = string.IsNullOrWhiteSpace(fieldsJson) ? "{}" : fieldsJson,
                Dnp = BoolValue(item, "dnp"),
                ExcludeFromBom = BoolValue(item, "excludeFromBom"),
                ExcludeFromBoard = BoolValue(item, "excludeFromBoard"),
                MatchMethod = "none"
            };
        }

        private static Guid[] Uuids(JsonElement item)
        {
            if (!ProjectExport.TryProperty(item, "symbolUuids", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<Guid>();
            }

            List<Guid> ids = new();
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && Guid.TryParse(entry.GetString(), out Guid parsed))
                {
                    ids.Add(parsed);
                }
            }

            return ids.ToArray();
        }

        private static string? StringValue(JsonElement item, string name)
        {
            if (!ProjectExport.TryProperty(item, name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }

        private static bool BoolValue(JsonElement item, string name)
        {
            if (!ProjectExport.TryProperty(item, name, out JsonElement value))
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed))
            {
                return parsed;
            }

            return false;
        }

        private static string? Field(JsonElement fields, string name)
        {
            foreach (JsonProperty property in fields.EnumerateObject())
            {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                if (property.Value.ValueKind == JsonValueKind.Object
                    && ProjectExport.TryProperty(property.Value, "value", out JsonElement inner)
                    && inner.ValueKind == JsonValueKind.String)
                {
                    return inner.GetString();
                }
            }

            return null;
        }
    }
}
