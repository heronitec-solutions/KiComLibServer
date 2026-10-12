using System.Globalization;
using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    internal static class DistributorJson
    {
        public static bool Try(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        public static string? Text(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!Try(element, name, out JsonElement value))
                {
                    continue;
                }

                string? text = AsText(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            return null;
        }

        public static string? AsText(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        public static int? Int(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!Try(element, name, out JsonElement value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal decimalNumber)
                    && decimalNumber == decimal.Truncate(decimalNumber)
                    && decimalNumber <= int.MaxValue
                    && decimalNumber >= int.MinValue)
                {
                    return (int)decimalNumber;
                }

                if (value.ValueKind == JsonValueKind.String
                    && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        public static bool Flag(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!Try(element, name, out JsonElement value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    string? text = value.GetString();
                    if (bool.TryParse(text, out bool parsed))
                    {
                        return parsed;
                    }

                    if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(text, "1", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static JsonElement? Array(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (Try(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
                {
                    return value;
                }
            }

            return null;
        }
    }
}
