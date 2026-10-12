namespace KiComLibBackend.Services
{
    public static class PasswordPolicy
    {
        public const int MinimumLength = 12;

        /// <summary>
        /// Password installed with a new database. Sign-in still accepts it once,
        /// and every other request is refused until it is replaced.
        /// </summary>
        public const string DefaultPassword = "kicomlib";

        public const string DefaultHash =
            "pbkdf2-sha256$210000$S2lDb21MaWJEZWZhdWx0MDEh$YmQ8zYnT28KrSBN7lde4pCY4uSzWLd/93RTsEKXv4k8=";

        public static bool IsDefaultHash(string? stored)
        {
            return string.Equals(stored, DefaultHash, StringComparison.Ordinal);
        }

        public static bool MustChange(string? source, string? passwordHash)
        {
            return !string.Equals(source, "directory", StringComparison.Ordinal)
                && IsDefaultHash(passwordHash);
        }

        public const string DefaultPasswordError = "kicomlib is the installed default password and cannot be used.";

        public static string? ValidateNew(string? password)
        {
            string value = (password ?? "").Trim();
            if (string.Equals(value, DefaultPassword, StringComparison.Ordinal))
            {
                return DefaultPasswordError;
            }

            if (value.Length < MinimumLength)
            {
                return "Password must be at least " + MinimumLength + " characters";
            }

            return null;
        }
    }
}
