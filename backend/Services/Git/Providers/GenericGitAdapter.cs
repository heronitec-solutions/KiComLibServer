using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public sealed class GenericGitAdapter : IGitProviderAdapter
    {
        public string Kind => "generic";

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
            return new RepositoryLinks();
        }

        public bool VerifyWebhook(HttpRequest request, ReadOnlySpan<byte> body, string secret)
        {
            return false;
        }

        public WebhookEvent ParseWebhook(HttpRequest request, ReadOnlySpan<byte> body)
        {
            return new WebhookEvent { Kind = "ignored" };
        }

        public SetupInstructions Instructions(GitRepository repository, string webhookUrl)
        {
            return new SetupInstructions
            {
                Summary = "This host has no webhook. KiComLib polls the repository.",
                Steps = new[] { "Leave the sync mode on poll. KiComLib will check for new commits on the poll interval." }
            };
        }
    }
}
