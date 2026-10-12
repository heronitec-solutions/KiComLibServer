using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KiComLibBackend.Services
{
    public static class GitText
    {
        private static readonly Regex AuthorizationPattern = new(
            @"Authorization:\s*(?:Basic|Bearer)\s+\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TokenPattern = new(
            @"\b(?:glpat-|github_pat_|ghp_|gho_|ghu_|ghs_|ghr-)[A-Za-z0-9_\-]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Scrub(string? text, IEnumerable<string?> secrets)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? "";
            }

            string result = text;
            foreach (string? secret in secrets)
            {
                if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
                {
                    result = result.Replace(secret, "***", StringComparison.Ordinal);
                }
            }

            result = AuthorizationPattern.Replace(result, "Authorization: ***");
            result = TokenPattern.Replace(result, "***");
            return result;
        }

        public static bool FixedTimeEquals(string? provided, string expected)
        {
            byte[] actual = Encoding.UTF8.GetBytes(provided ?? "");
            byte[] want = Encoding.UTF8.GetBytes(expected ?? "");
            if (actual.Length != want.Length)
            {
                CryptographicOperations.FixedTimeEquals(want, want);
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(actual, want);
        }

        public static string TokenHint(string secret)
        {
            string trimmed = secret.Trim();
            if (trimmed.Length <= 8)
            {
                return "…";
            }

            int prefixLength = Math.Min(6, trimmed.Length - 4);
            int dash = trimmed.IndexOf('-');
            if (dash > 0 && dash <= 12 && dash + 1 + 4 < trimmed.Length)
            {
                prefixLength = dash + 1;
            }

            return trimmed[..prefixLength] + "…" + trimmed[^4..];
        }

        public static string OpenSshFingerprint(string publicKeyLine)
        {
            string[] parts = publicKeyLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("The public key is not in OpenSSH format.");
            }

            byte[] blob = Convert.FromBase64String(parts[1]);
            string encoded = Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
            return "SHA256:" + encoded;
        }

        public static bool GlobMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
            {
                return true;
            }

            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static bool IsAuthenticationFailure(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
                || text.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase)
                || text.Contains("HTTP Basic: Access denied", StringComparison.OrdinalIgnoreCase)
                || text.Contains("The requested URL returned error: 401", StringComparison.OrdinalIgnoreCase)
                || text.Contains("The requested URL returned error: 403", StringComparison.OrdinalIgnoreCase)
                || text.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase);
        }

        public static string NewWebhookSecret()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        }
    }

    public sealed class MovedTagNotice
    {
        public string Name { get; set; } = "";

        public string From { get; set; } = "";

        public string To { get; set; } = "";
    }

    /// <summary>
    /// Moved tags are kept in git_repository.last_error as JSON so a later GET can warn
    /// without a separate column. A real fetch error is stored in the same document.
    /// </summary>
    public static class RepositoryNotices
    {
        public static string? Pack(IReadOnlyList<MovedTagNotice> tags, string? error)
        {
            if (tags.Count == 0)
            {
                return string.IsNullOrWhiteSpace(error) ? null : error;
            }

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                movedTags = tags.Select(tag => new { name = tag.Name, from = tag.From, to = tag.To }),
                error
            });
        }

        public static (string? Error, List<MovedTagNotice> Tags) Unpack(string? lastError)
        {
            List<MovedTagNotice> tags = new();
            if (string.IsNullOrWhiteSpace(lastError))
            {
                return (null, tags);
            }

            string trimmed = lastError.TrimStart();
            if (!trimmed.StartsWith('{'))
            {
                return (lastError, tags);
            }

            try
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(lastError);
                System.Text.Json.JsonElement root = document.RootElement;
                if (!root.TryGetProperty("movedTags", out System.Text.Json.JsonElement array) || array.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return (lastError, tags);
                }

                foreach (System.Text.Json.JsonElement item in array.EnumerateArray())
                {
                    tags.Add(new MovedTagNotice
                    {
                        Name = item.TryGetProperty("name", out System.Text.Json.JsonElement name) ? name.GetString() ?? "" : "",
                        From = item.TryGetProperty("from", out System.Text.Json.JsonElement from) ? from.GetString() ?? "" : "",
                        To = item.TryGetProperty("to", out System.Text.Json.JsonElement to) ? to.GetString() ?? "" : ""
                    });
                }

                string? error = null;
                if (root.TryGetProperty("error", out System.Text.Json.JsonElement errorElement) && errorElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    error = errorElement.GetString();
                }

                return (string.IsNullOrWhiteSpace(error) ? null : error, tags);
            }
            catch (System.Text.Json.JsonException)
            {
                return (lastError, tags);
            }
        }
    }
}
