using System.Text.Json;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Part links are web pages, such as a development board. Only http and https addresses are stored.
    /// </summary>
    public static class PartLinks
    {
        public const int MaxCount = 30;
        public const int MaxLabelLength = 200;
        public const int MaxUrlLength = 2000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public static string? Normalize(List<PartLink>? links, out List<PartLink> cleaned)
        {
            cleaned = new List<PartLink>();
            foreach (PartLink link in links ?? new List<PartLink>())
            {
                string label = (link.Label ?? "").Trim();
                string url = (link.Url ?? "").Trim();
                if (label.Length == 0 && url.Length == 0)
                {
                    continue;
                }

                if (label.Length > MaxLabelLength)
                {
                    return "The link name is too long.";
                }

                if (url.Length == 0 || url.Length > MaxUrlLength || !IsWebAddress(url))
                {
                    return "Each link needs an http or https address.";
                }

                if (cleaned.Any(existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                cleaned.Add(new PartLink { Label = label, Url = url });
            }

            if (cleaned.Count > MaxCount)
            {
                return "A part can have at most 30 links.";
            }

            return null;
        }

        public static string Serialize(List<PartLink>? links) =>
            JsonSerializer.Serialize(links ?? new List<PartLink>(), JsonOptions);

        public static List<PartLink> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return new List<PartLink>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<PartLink>>(json, JsonOptions) ?? new List<PartLink>();
            }
            catch (JsonException)
            {
                return new List<PartLink>();
            }
        }

        private static bool IsWebAddress(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }
    }
}
