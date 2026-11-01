using System.Text.Json;

namespace KiComLibBackend.Services.Distributors
{
    /// <summary>
    /// Turns a distributor error body into the sentence shown on the BOM.
    /// An HTTP 429, or a message about a rate or call limit, says the request limit is exhausted.
    /// </summary>
    internal static class DistributorFailure
    {
        public static string? Describe(int statusCode, string? message)
        {
            string? text = Clean(message);
            if (!IsLimit(statusCode, text))
            {
                return text;
            }

            return text == null
                ? "The API request limit is exhausted."
                : "The API request limit is exhausted: " + text;
        }

        public static string? ReadMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            string trimmed = body.Trim();
            try
            {
                using JsonDocument document = JsonDocument.Parse(trimmed);
                return First(document.RootElement);
            }
            catch (JsonException)
            {
                if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
                {
                    return null;
                }

                return Clean(trimmed);
            }
        }

        public static bool IsLimit(int statusCode, string? message)
        {
            if (statusCode == 429)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            if (message.Contains("E_LIMIT", StringComparison.OrdinalIgnoreCase)
                || message.Contains("too many", StringComparison.OrdinalIgnoreCase)
                || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || message.Contains("request limit", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            bool quota = message.Contains("call", StringComparison.OrdinalIgnoreCase)
                || message.Contains("request", StringComparison.OrdinalIgnoreCase)
                || message.Contains("quota", StringComparison.OrdinalIgnoreCase);
            bool exceeded = message.Contains("maximum", StringComparison.OrdinalIgnoreCase)
                || message.Contains("exceed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("exhaust", StringComparison.OrdinalIgnoreCase);
            return quota && exceeded;
        }

        private static string? First(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? nested = First(item);
                    if (nested != null)
                    {
                        return nested;
                    }
                }

                return null;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return element.ValueKind == JsonValueKind.String ? Clean(element.GetString()) : null;
            }

            foreach (string name in new[] { "Errors", "errors" })
            {
                JsonElement? errors = DistributorJson.Array(element, name);
                if (errors == null)
                {
                    continue;
                }

                foreach (JsonElement error in errors.Value.EnumerateArray())
                {
                    string? nested = error.ValueKind == JsonValueKind.Object
                        ? Clean(DistributorJson.Text(error, "Message", "message", "Code", "code"))
                        : First(error);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            foreach (string name in new[] { "error", "Error", "fault", "Fault" })
            {
                if (!DistributorJson.Try(element, name, out JsonElement child))
                {
                    continue;
                }

                if (child.ValueKind == JsonValueKind.String)
                {
                    return Clean(DistributorJson.Text(
                        element,
                        "error_description",
                        "ErrorDetails",
                        "errorMessage",
                        "ErrorMessage",
                        "message",
                        "Message"))
                        ?? Clean(child.GetString());
                }

                if (child.ValueKind == JsonValueKind.Object)
                {
                    string? nested = Clean(DistributorJson.Text(
                        child,
                        "message",
                        "Message",
                        "errorMessage",
                        "ErrorMessage",
                        "detail",
                        "description"))
                        ?? First(child);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return Clean(DistributorJson.Text(
                element,
                "error_description",
                "ErrorDetails",
                "errorMessage",
                "ErrorMessage",
                "Message",
                "message",
                "detail",
                "title"));
        }

        private static string? Clean(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            string text = message.Trim().Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 300 ? text : text[..300];
        }
    }

    /// <summary>Splits manufacturer part numbers into the batches one distributor call can carry.</summary>
    internal static class PartBatches
    {
        public static IReadOnlyList<IReadOnlyList<string>> Chunk(
            IReadOnlyList<string> partNumbers,
            int limit,
            Func<string, bool>? alone = null)
        {
            List<IReadOnlyList<string>> packed = new();
            List<string> batch = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in partNumbers)
            {
                string part = (raw ?? "").Trim();
                if (part.Length == 0 || !seen.Add(part))
                {
                    continue;
                }

                if (alone != null && alone(part))
                {
                    Flush();
                    packed.Add(new[] { part });
                    continue;
                }

                batch.Add(part);
                if (batch.Count == limit)
                {
                    Flush();
                }
            }

            Flush();
            return packed;

            void Flush()
            {
                if (batch.Count == 0)
                {
                    return;
                }

                packed.Add(batch);
                batch = new List<string>();
            }
        }
    }
}
