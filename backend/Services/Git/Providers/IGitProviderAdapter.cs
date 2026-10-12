using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class RepositoryAddress
    {
        public string CloneUrl { get; init; } = "";

        public string? WebUrl { get; init; }

        public string ProviderPath { get; init; } = "";
    }

    public sealed class RepositoryLinks
    {
        public string? Commit { get; init; }

        public string? Compare { get; init; }

        public string? Tag { get; init; }
    }

    public sealed class WebhookEvent
    {
        public string Kind { get; init; } = "ignored";

        public IReadOnlyList<string> ChangedRefs { get; init; } = Array.Empty<string>();
    }

    public sealed class SetupInstructions
    {
        public string Summary { get; init; } = "";

        public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    }

    public interface IGitProviderAdapter
    {
        string Kind { get; }

        RepositoryAddress Parse(GitHost host, string cloneUrl);

        RepositoryLinks Links(GitRepository repository);

        bool VerifyWebhook(HttpRequest request, ReadOnlySpan<byte> body, string secret);

        WebhookEvent ParseWebhook(HttpRequest request, ReadOnlySpan<byte> body);

        SetupInstructions Instructions(GitRepository repository, string webhookUrl);
    }

    public sealed class GitAccessException : Exception
    {
        public GitAccessException(string message) : base(message)
        {
        }
    }
}
