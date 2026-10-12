using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public static class GitUrlPolicy
    {
        public static bool FileProtocolAllowed =>
            string.Equals(Environment.GetEnvironmentVariable("GIT_ALLOW_FILE_PROTOCOL"), "true", StringComparison.OrdinalIgnoreCase);

        public static bool TryParseFile(string cloneUrl, out RepositoryAddress address)
        {
            address = new RepositoryAddress();
            string trimmed = cloneUrl.Trim();
            if (!trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!FileProtocolAllowed)
            {
                throw new GitAccessException("file:// clone URLs are disabled.");
            }

            string path = trimmed["file:".Length..].TrimStart('/');
            if (OperatingSystem.IsWindows() && path.Length >= 2 && path[1] == ':')
            {
                // file:///C:/repo stays C:/repo after trimming the slash that Uri adds.
            }
            else if (trimmed.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted("/" + path))
            {
                path = "/" + path;
            }

            path = path.Replace('/', Path.DirectorySeparatorChar);
            string providerPath = path.Replace('\\', '/').Trim('/');
            address = new RepositoryAddress
            {
                CloneUrl = trimmed.Replace('\\', '/'),
                WebUrl = null,
                ProviderPath = string.IsNullOrEmpty(providerPath) ? "local" : providerPath
            };
            return true;
        }

        public static void RejectUserInfo(Uri uri)
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new GitAccessException("Clone URL must not contain credentials.");
            }
        }

        /// <summary>
        /// SSH clone URLs use an account name such as <c>git</c>. That is not a secret.
        /// A password or token (<c>user:secret</c>) still belongs in the credential store.
        /// </summary>
        public static void RejectSshCredentials(Uri uri)
        {
            string userInfo = Uri.UnescapeDataString(uri.UserInfo ?? "");
            if (userInfo.Contains(':'))
            {
                throw new GitAccessException("Clone URL must not contain credentials.");
            }
        }

        public static bool IsSshClone(string cloneUrl)
        {
            string trimmed = cloneUrl.Trim();
            if (trimmed.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryParseScp(trimmed, out _, out _, out _);
        }

        public static bool IsHttpClone(string cloneUrl)
        {
            string trimmed = cloneUrl.Trim();
            return trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        public static string? CredentialMismatch(string cloneUrl, string? credentialKind)
        {
            if (string.IsNullOrEmpty(credentialKind))
            {
                return null;
            }

            if (IsSshClone(cloneUrl) && credentialKind != "ssh_key")
            {
                return "This clone URL uses SSH. Select an SSH key, or use the HTTPS clone URL with an HTTPS token.";
            }

            if (IsHttpClone(cloneUrl) && credentialKind != "https_token")
            {
                return "This clone URL uses HTTPS. Select an HTTPS token, or use the SSH clone URL with an SSH key.";
            }

            return null;
        }

        /// <summary>
        /// Puts the HTTPS username on the URL git actually calls. The token stays in http.extraHeader.
        /// Without a username, git tries to prompt and fails because terminal prompts are disabled.
        /// </summary>
        public static string WithHttpsUser(string cloneUrl, string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return cloneUrl;
            }

            if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out Uri? uri))
            {
                return cloneUrl;
            }

            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return cloneUrl;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return cloneUrl;
            }

            UriBuilder builder = new(uri) { UserName = username.Trim() };
            if (uri.IsDefaultPort)
            {
                builder.Port = -1;
            }

            return builder.Uri.AbsoluteUri;
        }

        public static string DefaultHttpsUsername(string? hostKind)
        {
            return string.Equals(hostKind, "github", StringComparison.OrdinalIgnoreCase)
                ? "x-access-token"
                : "oauth2";
        }

        public static string TrimGitSuffix(string path)
        {
            string trimmed = path.Trim('/');
            if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^4];
            }

            return trimmed.Trim('/');
        }

        public static void EnsureHttpsHost(GitHost host, Uri uri)
        {
            RejectUserInfo(uri);
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                if (!host.AllowHttp)
                {
                    throw new GitAccessException("HTTP clone URLs are not allowed for this host.");
                }
            }
            else if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                throw new GitAccessException("Unsupported clone URL scheme '" + uri.Scheme + "'.");
            }

            if (!Uri.TryCreate(host.WebUrl, UriKind.Absolute, out Uri? web))
            {
                throw new GitAccessException("The git host web URL is invalid.");
            }

            if (!uri.Host.Equals(web.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new GitAccessException("Clone URL host does not match the selected git host.");
            }

            int actualPort = uri.IsDefaultPort ? (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? 80 : 443) : uri.Port;
            int expectedPort = web.IsDefaultPort ? (web.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? 80 : 443) : web.Port;
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && web.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && web.IsDefaultPort)
            {
                expectedPort = 80;
            }

            if (actualPort != expectedPort && !(web.IsDefaultPort && uri.IsDefaultPort && uri.Scheme.Equals(web.Scheme, StringComparison.OrdinalIgnoreCase)))
            {
                if (actualPort != expectedPort)
                {
                    throw new GitAccessException("Clone URL port does not match the selected git host.");
                }
            }
        }

        public static (string Host, int Port) ParseSshEndpoint(string? sshHost)
        {
            if (string.IsNullOrWhiteSpace(sshHost))
            {
                throw new GitAccessException("This git host has no SSH endpoint.");
            }

            string value = sshHost.Trim();
            if (value.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
                {
                    throw new GitAccessException("The git host SSH endpoint is invalid.");
                }

                return (uri.Host, uri.IsDefaultPort ? 22 : uri.Port);
            }

            string withoutUser = value;
            int at = value.LastIndexOf('@');
            if (at >= 0)
            {
                withoutUser = value[(at + 1)..];
            }

            int colon = withoutUser.LastIndexOf(':');
            if (colon > 0 && int.TryParse(withoutUser[(colon + 1)..], out int port))
            {
                return (withoutUser[..colon], port);
            }

            return (withoutUser, 22);
        }

        public static void EnsureSshHost(GitHost host, string hostname, int port)
        {
            (string expectedHost, int expectedPort) = ParseSshEndpoint(host.SshHost);
            if (!hostname.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) || port != expectedPort)
            {
                throw new GitAccessException("Clone URL host does not match the selected git host.");
            }
        }

        public static bool TryParseScp(string cloneUrl, out string host, out int port, out string path)
        {
            host = "";
            port = 22;
            path = "";
            string trimmed = cloneUrl.Trim();
            int colon = trimmed.IndexOf(':');
            if (colon <= 0 || trimmed.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            string left = trimmed[..colon];
            path = trimmed[(colon + 1)..].Trim('/');
            int at = left.LastIndexOf('@');
            host = at >= 0 ? left[(at + 1)..] : left;
            return host.Length > 0 && path.Length > 0 && !host.Contains('/');
        }
    }
}
