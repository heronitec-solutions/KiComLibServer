namespace KiComLibBackend.Services
{
    public static class ProductVersion
    {
        public const int ApiVersion = 2;

        public const string HttpLibraryApiVersion = "v1";

        public static string Read(string contentRootPath)
        {
            foreach (string candidate in Candidates(contentRootPath))
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                string version = File.ReadAllText(candidate).Trim();
                if (version.Length > 0)
                {
                    int lineBreak = version.IndexOfAny(new[] { '\r', '\n' });
                    return lineBreak < 0 ? version : version[..lineBreak].Trim();
                }
            }

            return "unknown";
        }

        private static IEnumerable<string> Candidates(string contentRootPath)
        {
            yield return Path.Combine(contentRootPath, "version.txt");
            yield return Path.GetFullPath(Path.Combine(contentRootPath, "..", "version.txt"));
            yield return Path.Combine(AppContext.BaseDirectory, "version.txt");
        }
    }
}
