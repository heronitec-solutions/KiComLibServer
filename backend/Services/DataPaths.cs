namespace KiComLibBackend.Services
{
    /// <summary>
    /// Resolves data directories the same way relative paths are meant to sit under the content root.
    /// An absolute environment value is used as given. A relative value is combined with the content root.
    /// </summary>
    public static class DataPaths
    {
        public static string Resolve(string contentRoot, string variable, params string[] defaultParts)
        {
            string? configured = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                string[] parts = new string[defaultParts.Length + 1];
                parts[0] = contentRoot;
                defaultParts.CopyTo(parts, 1);
                return Path.Combine(parts);
            }

            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            return Path.GetFullPath(Path.Combine(contentRoot, configured));
        }
    }
}
