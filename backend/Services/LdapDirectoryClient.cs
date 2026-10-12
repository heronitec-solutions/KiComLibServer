using System.DirectoryServices.Protocols;
using System.Net.Sockets;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// LDAP simple bind against Active Directory or a generic directory.
    /// The service account is used to search. The user password is checked with a second bind.
    /// </summary>
    public sealed class LdapDirectoryClient : IDirectoryClient
    {
        private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

        public Task TestAsync(DirectorySettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using LdapConnection connection = BindService(settings);
            return Task.CompletedTask;
        }

        public Task<DirectoryAccount?> AuthenticateAsync(
            DirectorySettings settings,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(password))
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            using LdapConnection connection = BindService(settings);
            string filter = DirectoryFilters.LoginFilter(settings, username);
            SearchResultEntryCollection entries = Search(
                connection,
                settings.UserBaseDn,
                filter,
                2,
                UserAttributes(settings));
            if (entries.Count != 1)
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            SearchResultEntry entry = entries[0];
            if (DirectoryFilters.IsDisabledAccount(ReadScalar(entry, "userAccountControl")))
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            DirectoryAccount? account = MapUser(entry, settings, connection, includeMembership: true);
            if (account == null)
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            try
            {
                using LdapConnection userBind = Bind(settings, entry.DistinguishedName, password);
            }
            catch (LdapException exception) when (exception.ErrorCode == 49)
            {
                return Task.FromResult<DirectoryAccount?>(null);
            }

            return Task.FromResult<DirectoryAccount?>(account);
        }

        public Task<IReadOnlyList<DirectoryAccount>> SearchUsersAsync(
            DirectorySettings settings,
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using LdapConnection connection = BindService(settings);
            SearchResultEntryCollection entries = Search(
                connection,
                settings.UserBaseDn,
                DirectoryFilters.UserSearchFilter(settings, query),
                50,
                UserAttributes(settings));
            List<DirectoryAccount> accounts = new();
            foreach (SearchResultEntry entry in entries)
            {
                DirectoryAccount? account = MapUser(entry, settings, connection, includeMembership: false);
                if (account != null)
                {
                    accounts.Add(account);
                }
            }

            return Task.FromResult<IReadOnlyList<DirectoryAccount>>(accounts);
        }

        public Task<IReadOnlyList<DirectoryGroupRecord>> SearchGroupsAsync(
            DirectorySettings settings,
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using LdapConnection connection = BindService(settings);
            SearchResultEntryCollection entries = Search(
                connection,
                settings.GroupBaseDn,
                DirectoryFilters.GroupSearchFilter(settings, query),
                50,
                GroupAttributes(settings));
            List<DirectoryGroupRecord> groups = new();
            foreach (SearchResultEntry entry in entries)
            {
                DirectoryGroupRecord? group = MapGroup(entry, settings);
                if (group != null)
                {
                    groups.Add(group);
                }
            }

            return Task.FromResult<IReadOnlyList<DirectoryGroupRecord>>(groups);
        }

        public Task<DirectoryAccount?> ReadUserAsync(
            DirectorySettings settings,
            string distinguishedName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using LdapConnection connection = BindService(settings);
            SearchResultEntry? entry = ReadEntry(connection, distinguishedName, UserAttributes(settings));
            return Task.FromResult(entry == null ? null : MapUser(entry, settings, connection, includeMembership: true));
        }

        public Task<DirectoryGroupRecord?> ReadGroupAsync(
            DirectorySettings settings,
            string distinguishedName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using LdapConnection connection = BindService(settings);
            SearchResultEntry? entry = ReadEntry(connection, distinguishedName, GroupAttributes(settings));
            return Task.FromResult(entry == null ? null : MapGroup(entry, settings));
        }

        private static LdapConnection BindService(DirectorySettings settings)
        {
            try
            {
                if (string.IsNullOrEmpty(settings.BindDn))
                {
                    return Bind(settings, null, null);
                }

                return Bind(settings, settings.BindDn, settings.BindPassword);
            }
            catch (LdapException exception) when (exception.ErrorCode == 49)
            {
                throw new DirectoryFailureException("The bind account was rejected.");
            }
            catch (Exception exception) when (IsTransportError(exception))
            {
                throw new DirectoryFailureException(Describe(exception), exception);
            }
        }

        private static LdapConnection Bind(DirectorySettings settings, string? dn, string? password)
        {
            LdapConnection connection = new(new LdapDirectoryIdentifier(settings.Host, settings.Port))
            {
                Timeout = CallTimeout,
                AuthType = string.IsNullOrEmpty(dn) ? AuthType.Anonymous : AuthType.Basic
            };
            try
            {
                connection.SessionOptions.ProtocolVersion = 3;
                connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
                if (settings.Encryption == "ssl")
                {
                    connection.SessionOptions.SecureSocketLayer = true;
                }

                if (settings.AllowUntrustedCertificate && settings.Encryption != "none")
                {
                    connection.SessionOptions.VerifyServerCertificate = static (_, _) => true;
                }

                if (settings.Encryption == "starttls")
                {
                    connection.SessionOptions.StartTransportLayerSecurity(null);
                }

                if (string.IsNullOrEmpty(dn))
                {
                    connection.Bind();
                }
                else
                {
                    connection.Bind(new System.Net.NetworkCredential(dn, password ?? ""));
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static SearchResultEntryCollection Search(
            LdapConnection connection,
            string baseDn,
            string filter,
            int sizeLimit,
            string[] attributes)
        {
            SearchRequest request = new(baseDn, filter, SearchScope.Subtree, attributes)
            {
                SizeLimit = sizeLimit,
                TimeLimit = TimeSpan.FromSeconds(10)
            };
            try
            {
                if (connection.SendRequest(request) is not SearchResponse response)
                {
                    throw new DirectoryFailureException("The directory server returned an unexpected response.");
                }

                return response.Entries;
            }
            catch (LdapException exception) when (exception.ErrorCode == 4)
            {
                throw new DirectoryFailureException("The directory search returned too many entries. Narrow the search.");
            }
            catch (Exception exception) when (IsTransportError(exception))
            {
                throw new DirectoryFailureException(Describe(exception), exception);
            }
        }

        private static SearchResultEntry? ReadEntry(LdapConnection connection, string distinguishedName, string[] attributes)
        {
            SearchRequest request = new(distinguishedName, "(objectClass=*)", SearchScope.Base, attributes)
            {
                TimeLimit = TimeSpan.FromSeconds(10)
            };
            try
            {
                if (connection.SendRequest(request) is not SearchResponse response || response.Entries.Count != 1)
                {
                    return null;
                }

                return response.Entries[0];
            }
            catch (LdapException exception) when (exception.ErrorCode is 32 or 34)
            {
                return null;
            }
            catch (Exception exception) when (IsTransportError(exception))
            {
                throw new DirectoryFailureException(Describe(exception), exception);
            }
        }

        private static DirectoryAccount? MapUser(
            SearchResultEntry entry,
            DirectorySettings settings,
            LdapConnection connection,
            bool includeMembership)
        {
            string username = (ReadScalar(entry, settings.UsernameAttribute) ?? "").Trim();
            if (username.Length < 1 || username.Length > 100)
            {
                return null;
            }

            string id = (ReadScalar(entry, settings.UserIdAttribute) ?? entry.DistinguishedName).Trim().ToLowerInvariant();
            if (id.Length < 1 || id.Length > 200)
            {
                return null;
            }

            string display = (ReadScalar(entry, settings.DisplayNameAttribute) ?? ReadScalar(entry, "cn") ?? username).Trim();
            if (display.Length > 200)
            {
                display = display[..200];
            }

            IReadOnlyList<string> groups = includeMembership
                ? Membership(entry, settings, connection)
                : Array.Empty<string>();
            return new DirectoryAccount
            {
                Id = id,
                Username = username,
                DisplayName = display,
                DistinguishedName = entry.DistinguishedName,
                GroupDistinguishedNames = groups
            };
        }

        private static DirectoryGroupRecord? MapGroup(SearchResultEntry entry, DirectorySettings settings)
        {
            string name = (ReadScalar(entry, settings.GroupNameAttribute) ?? ReadScalar(entry, "cn") ?? "").Trim();
            if (name.Length < 1 || name.Length > 100)
            {
                return null;
            }

            string id = (ReadScalar(entry, settings.GroupIdAttribute) ?? entry.DistinguishedName).Trim().ToLowerInvariant();
            if (id.Length < 1 || id.Length > 200)
            {
                return null;
            }

            string description = (ReadScalar(entry, "description") ?? "").Trim();
            if (description.Length > 500)
            {
                description = description[..500];
            }

            return new DirectoryGroupRecord
            {
                Id = id,
                Name = name,
                DistinguishedName = entry.DistinguishedName,
                Description = description
            };
        }

        private static IReadOnlyList<string> Membership(
            SearchResultEntry entry,
            DirectorySettings settings,
            LdapConnection connection)
        {
            if (settings.MembershipAttribute.Equals("memberOf", StringComparison.OrdinalIgnoreCase))
            {
                return ReadStrings(entry, settings.MembershipAttribute);
            }

            string filter = $"(&{settings.GroupFilter}({settings.MembershipAttribute}={DirectoryFilters.Escape(entry.DistinguishedName)}))";
            SearchResultEntryCollection groups = Search(connection, settings.GroupBaseDn, filter, 200, Array.Empty<string>());
            List<string> names = new();
            foreach (SearchResultEntry group in groups)
            {
                if (!string.IsNullOrWhiteSpace(group.DistinguishedName))
                {
                    names.Add(group.DistinguishedName);
                }
            }

            return names;
        }

        private static string[] UserAttributes(DirectorySettings settings)
        {
            List<string> names = new()
            {
                settings.UsernameAttribute,
                settings.DisplayNameAttribute,
                settings.UserIdAttribute,
                "cn",
                "userAccountControl"
            };
            if (settings.MembershipAttribute.Equals("memberOf", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(settings.MembershipAttribute);
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] GroupAttributes(DirectorySettings settings)
        {
            return new[]
            {
                settings.GroupNameAttribute,
                settings.GroupIdAttribute,
                "cn",
                "description"
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string? ReadScalar(SearchResultEntry entry, string attribute)
        {
            IReadOnlyList<string> values = ReadStrings(entry, attribute);
            return values.Count == 0 ? null : values[0];
        }

        private static IReadOnlyList<string> ReadStrings(SearchResultEntry entry, string attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute) || !entry.Attributes.Contains(attribute))
            {
                return Array.Empty<string>();
            }

            DirectoryAttribute values = entry.Attributes[attribute];
            List<string> result = new();
            for (int i = 0; i < values.Count; i++)
            {
                object raw = values[i];
                if (raw is byte[] bytes)
                {
                    if (attribute.Equals("objectGUID", StringComparison.OrdinalIgnoreCase) && bytes.Length == 16)
                    {
                        result.Add(new Guid(bytes).ToString("D"));
                    }
                    else if (attribute.Equals("userAccountControl", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(DecodeInteger(bytes));
                    }
                    else
                    {
                        result.Add(Convert.ToHexString(bytes).ToLowerInvariant());
                    }
                }
                else if (raw != null)
                {
                    string text = raw.ToString() ?? "";
                    if (text.Length > 0)
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        private static string DecodeInteger(byte[] bytes)
        {
            if (bytes.Length == 4)
            {
                return BitConverter.ToInt32(bytes, 0).ToString();
            }

            return System.Text.Encoding.ASCII.GetString(bytes);
        }

        private static bool IsTransportError(Exception exception)
        {
            return exception is LdapException or DirectoryOperationException or IOException or SocketException or TimeoutException or System.Security.Authentication.AuthenticationException;
        }

        private static string Describe(Exception exception)
        {
            if (exception is LdapException ldap)
            {
                string detail = string.IsNullOrWhiteSpace(ldap.ServerErrorMessage) ? ldap.Message : ldap.ServerErrorMessage.Trim();
                return $"The directory server refused the request ({ldap.ErrorCode}). {detail}";
            }

            if (exception is DirectoryOperationException operation && !string.IsNullOrWhiteSpace(operation.Message))
            {
                return operation.Message;
            }

            return "The directory server could not be reached.";
        }
    }
}
