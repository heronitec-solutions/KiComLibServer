using System.Text.RegularExpressions;

namespace KiComLibBackend.Services.Distributors
{
    public static class DistributorLog
    {
        private static readonly Regex SecretQuery = new(
            @"(?i)(apiKey|apikey|client_secret)=([^&#\s]*)",
            RegexOptions.Compiled);

        private static readonly Regex SecretJson = new(
            @"(?i)(""apikey""\s*:\s*"")[^""]*("")",
            RegexOptions.Compiled);

        private static readonly Regex SecretEncoded = new(
            @"(?i)(apikey%22%3[Aa]%22)[^%&]*(%22)",
            RegexOptions.Compiled);

        public static string Sanitize(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string hidden = SecretQuery.Replace(text, "$1=***");
            hidden = SecretJson.Replace(hidden, "$1***$2");
            return SecretEncoded.Replace(hidden, "$1***$2");
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
