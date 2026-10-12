namespace KiComLibBackend.Services.Distributors
{
    /// <summary>
    /// Maps distributor lifecycle text onto active, nrnd, ltb, or eol.
    /// </summary>
    public static class LifecycleMapper
    {
        public static string? Map(string? status, bool discontinued = false)
        {
            if (discontinued)
            {
                return "eol";
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            string text = status.Trim().ToLowerInvariant();
            if (text.Contains("not recommended")
                || text.Contains("not for new designs")
                || text.Contains("nrnd"))
            {
                return "nrnd";
            }

            if (text.Contains("last time buy") || text.Contains("ltb"))
            {
                return "ltb";
            }

            if (text.Contains("end of life")
                || text.Contains("obsolete")
                || text.Contains("discontinued"))
            {
                return "eol";
            }

            if (text.Contains("inactive"))
            {
                return null;
            }

            if (text.Contains("new product") || text.Contains("active") || text.Contains("production"))
            {
                return "active";
            }

            return null;
        }
    }
}
