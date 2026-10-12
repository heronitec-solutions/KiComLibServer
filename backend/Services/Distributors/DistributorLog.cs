using System.Text.RegularExpressions;

namespace KiComLibBackend.Services.Distributors
{
    public static class DistributorLog
    {
        private static readonly Regex SecretQuery = new(
            @"(?i)(apiKey|apikey|client_secret)=([^&#\s]*)",
            RegexOptions.Compiled);

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return SecretQuery.Replace(text, "$1=***");
        }
    }

    public sealed class DistributorUpstreamException : Exception
    {
        public DistributorUpstreamException(string providerId, string message)
            : base(message)
        {
            ProviderId = providerId;
        }

        public string ProviderId { get; }
    }
}
