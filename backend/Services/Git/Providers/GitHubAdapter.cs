using System.Text.Json;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class GitHubAdapter : IGitProviderAdapter
    {
        public string Kind => "github";

        public RepositoryAddress Parse(GitHost host, string cloneUrl)
        {
            if (GitUrlPolicy.TryParseFile(cloneUrl, out RepositoryAddress file))
            {
                return file;
            }

            return GitLabAdapter.ParseRemote(host, cloneUrl);
        }

        public RepositoryLinks Links(GitRepository repository)
        {
            string? web = string.IsNullOrWhiteSpace(repository.WebUrl) ? null : repository.WebUrl.TrimEnd('/');
            if (web == null)
            {
                return new RepositoryLinks();
            }

            return new RepositoryLinks
            {
                Commit = web + "/commit/{sha}",
                Compare = web + "/compare/{base}...{target}",
                Tag = web + "/releases/tag/{tag}"
            };
        }

        public bool VerifyWebhook(HttpRequest request, ReadOnlySpan<byte> body, string secret)
        {
            if (!request.Headers.TryGetValue("X-Hub-Signature-256", out Microsoft.Extensions.Primitives.StringValues values))
            {
                return false;
            }

            string provided = values.ToString().Trim();
            byte[] hash = System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), body);
            string expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
            return GitText.FixedTimeEquals(provided, expected);
        }

        public WebhookEvent ParseWebhook(HttpRequest request, ReadOnlySpan<byte> body)
        {
            string eventName = request.Headers.TryGetValue("X-GitHub-Event", out Microsoft.Extensions.Primitives.StringValues values)
                ? values.ToString()
                : "";
            if (eventName.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                return new WebhookEvent { Kind = "ping" };
            }

            if (!eventName.Equals("push", StringComparison.OrdinalIgnoreCase))
            {
                return new WebhookEvent { Kind = "ignored" };
            }

            IReadOnlyList<string> refs = ReadRef(body);
            string kind = refs.Any(r => r.StartsWith("refs/tags/", StringComparison.Ordinal)) ? "tag" : "push";
            return new WebhookEvent { Kind = kind, ChangedRefs = refs };
        }

        public SetupInstructions Instructions(GitRepository repository, string webhookUrl)
        {
            return new SetupInstructions
            {
                Summary = "Add a GitHub webhook that sends JSON push events.",
                Steps = new[]
                {
                    "Open the repository in GitHub and go to Settings → Webhooks → Add webhook.",
                    "Payload URL: " + webhookUrl,
                    "Content type: application/json.",
                    "Secret: the value shown once when this repository was created or the secret was rotated.",
                    "Events: Just the push event (this covers branches and tags)."
                }
            };
        }

        private static IReadOnlyList<string> ReadRef(ReadOnlySpan<byte> body)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body.ToArray());
                if (document.RootElement.TryGetProperty("ref", out JsonElement reference) && reference.ValueKind == JsonValueKind.String)
                {
                    string? name = reference.GetString();
                    return string.IsNullOrEmpty(name) ? Array.Empty<string>() : new[] { name };
                }
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }

            return Array.Empty<string>();
        }
    }
}
