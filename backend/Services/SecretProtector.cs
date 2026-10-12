using Microsoft.AspNetCore.DataProtection;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Encrypts git tokens, SSH private keys, and webhook secrets.
    /// The API returns only a hint; the protected value is what is stored.
    /// </summary>
    public sealed class SecretProtector
    {
        public const string Purpose = "KiComLib.Git.Credential.v1";

        private readonly IDataProtector _protector;

        public SecretProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(Purpose);
        }

        public string Protect(string plaintext)
        {
            return _protector.Protect(plaintext);
        }

        public string Unprotect(string protectedText)
        {
            return _protector.Unprotect(protectedText);
        }
    }
}
