using System.Text.Json;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class GitLabAdapter : IGitProviderAdapter
    {
        public string Kind => "gitlab";

        public RepositoryAddress Parse(GitHost host, string cloneUrl)
        {
            if (GitUrlPolicy.TryParseFile(cloneUrl, out RepositoryAddress file))
            {
                return file;
            }

            return ParseRemote(host, cloneUrl);
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
                Commit = web + "/-/commit/{sha}",
                Compare = web + "/-/compare/{base}...{target}",
                Tag = web + "/-/tags/{tag}"
            };
        }

        public bool VerifyWebhook(HttpRequest request, ReadOnlySpan<byte> body, string secret)
        {
            if (!request.Headers.TryGetValue("X-Gitlab-Token", out Microsoft.Extensions.Primitives.StringValues values))
            {
                return false;
            }

            return GitText.FixedTimeEquals(values.ToString(), secret);
        }

        public WebhookEvent ParseWebhook(HttpRequest request, ReadOnlySpan<byte> body)
        {
            string eventName = request.Headers.TryGetValue("X-Gitlab-Event", out Microsoft.Extensions.Primitives.StringValues values)
                ? values.ToString()
                : "";
            if (eventName.Equals("Push Hook", StringComparison.OrdinalIgnoreCase)
                || eventName.Equals("Tag Push Hook", StringComparison.OrdinalIgnoreCase))
            {
                return new WebhookEvent
                {
                    Kind = eventName.Contains("Tag", StringComparison.OrdinalIgnoreCase) ? "tag" : "push",
                    ChangedRefs = ReadRef(body)
                };
            }

            return new WebhookEvent { Kind = "ignored" };
        }

        public SetupInstructions Instructions(GitRepository repository, string webhookUrl)
        {
            return new SetupInstructions
            {
                Summary = "Add a GitLab webhook for push and tag events.",
                Steps = new[]
                {
                    "Open the project in GitLab and go to Settings → Webhooks.",
                    "URL: " + webhookUrl,
                    "Secret token: the value shown once when this repository was created or the secret was rotated.",
                    "Trigger: Push events and Tag push events.",
                    "If KiComLib is on a private network, a GitLab administrator may need to allow webhook requests to that network."
                }
            };
        }

        internal static RepositoryAddress ParseRemote(GitHost host, string cloneUrl)
        {
            string trimmed = cloneUrl.Trim();
            if (trimmed.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? ssh))
                {
                    throw new GitAccessException("The clone URL is invalid.");
                }

                GitUrlPolicy.RejectSshCredentials(ssh);
                GitUrlPolicy.EnsureSshHost(host, ssh.Host, ssh.IsDefaultPort ? 22 : ssh.Port);
                string path = GitUrlPolicy.TrimGitSuffix(ssh.AbsolutePath);
                return Address(host, trimmed, path);
            }

            if (GitUrlPolicy.TryParseScp(trimmed, out string scpHost, out int scpPort, out string scpPath))
            {
                GitUrlPolicy.EnsureSshHost(host, scpHost, scpPort);
                return Address(host, trimmed, GitUrlPolicy.TrimGitSuffix(scpPath));
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? https))
            {
                throw new GitAccessException("The clone URL is invalid.");
            }

            GitUrlPolicy.EnsureHttpsHost(host, https);
            return Address(host, trimmed, GitUrlPolicy.TrimGitSuffix(https.AbsolutePath));
        }

        private static RepositoryAddress Address(GitHost host, string cloneUrl, string providerPath)
        {
            if (string.IsNullOrWhiteSpace(providerPath))
            {
                throw new GitAccessException("The clone URL does not contain a repository path.");
            }

            string web = host.WebUrl.TrimEnd('/') + "/" + providerPath;
            return new RepositoryAddress { CloneUrl = cloneUrl, WebUrl = web, ProviderPath = providerPath };
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
