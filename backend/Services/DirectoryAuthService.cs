using System.Text.RegularExpressions;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public sealed class DirectoryImportResult
    {
        public List<object> Imported { get; } = new();

        public List<object> Errors { get; } = new();
    }

    /// <summary>
    /// Local passwords stay on local accounts. Directory accounts are checked against
    /// LDAP or Active Directory and then stored here so groups and tokens keep working.
    /// </summary>
    public class DirectoryAuthService
    {
        private static readonly Regex AttributeName = new("^[A-Za-z][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

        private readonly ApplicationDbContext _db;
        private readonly SecretProtector _secrets;
        private readonly IDirectoryClient _directory;
        private readonly ILogger<DirectoryAuthService> _logger;

        public DirectoryAuthService(
            ApplicationDbContext db,
            SecretProtector secrets,
            IDirectoryClient directory,
            ILogger<DirectoryAuthService> logger)
        {
            _db = db;
            _secrets = secrets;
            _directory = directory;
            _logger = logger;
        }

        public async Task<AppUser?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
        {
            username = username.Trim();
            if (username.Length == 0 || password.Length == 0)
            {
                return null;
            }

            AppUser? existing = await _db.AppUsers.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
            if (existing != null && existing.Source != "directory")
            {
                if (!existing.IsActive || !PasswordHasher.Verify(password, existing.PasswordHash))
                {
                    return null;
                }

                await _db.Entry(existing).Collection(u => u.Groups).LoadAsync(cancellationToken);
                return existing;
            }

            DirectoryConnection? row = await _db.DirectoryConnections.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
            if (row == null || !row.Enabled)
            {
                return null;
            }

            DirectoryAccount? account;
            try
            {
                account = await _directory.AuthenticateAsync(SettingsFrom(row), username, password, cancellationToken);
            }
            catch (DirectoryFailureException exception)
            {
                _logger.LogWarning(exception, "Directory sign-in failed");
                return null;
            }

            if (account == null)
            {
                return null;
            }

            (AppUser? user, string? error) = await UpsertUserAsync(account, row.ProvisionOnLogin, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("Directory account {Username} was not signed in: {Error}", account.Username, error);
                return null;
            }

            if (!user.IsActive)
            {
                return null;
            }

            await SyncGroupsAsync(user, account.GroupDistinguishedNames, cancellationToken);
            return user;
        }

        public async Task<object> PayloadAsync(CancellationToken cancellationToken)
        {
            DirectoryConnection row = await RowAsync(cancellationToken);
            return ToPayload(row);
        }

        public async Task<string?> SaveAsync(DirectoryWriteRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return "Directory settings are required";
            }

            DirectoryConnection row = await RowAsync(cancellationToken);
            string? error = Validate(request, passwordAlreadyStored: !string.IsNullOrEmpty(row.BindPasswordProtected));
            if (error != null)
            {
                return error;
            }

            row.Enabled = request.Enabled;
            row.ServerType = (request.ServerType ?? "ldap").Trim().ToLowerInvariant();
            row.Host = (request.Host ?? "").Trim();
            row.Port = request.Port;
            row.Encryption = (request.Encryption ?? "none").Trim().ToLowerInvariant();
            row.AllowUntrustedCertificate = request.AllowUntrustedCertificate && row.Encryption != "none";
            row.BindDn = (request.BindDn ?? "").Trim();
            row.UserBaseDn = (request.UserBaseDn ?? "").Trim();
            row.UserFilter = FirstNonEmpty(request.UserFilter, row.UserFilter);
            row.UsernameAttribute = FirstNonEmpty(request.UsernameAttribute, row.UsernameAttribute);
            row.DisplayNameAttribute = FirstNonEmpty(request.DisplayNameAttribute, row.DisplayNameAttribute);
            row.UserIdAttribute = FirstNonEmpty(request.UserIdAttribute, row.UserIdAttribute);
            row.GroupBaseDn = (request.GroupBaseDn ?? "").Trim();
            row.GroupFilter = FirstNonEmpty(request.GroupFilter, row.GroupFilter);
            row.GroupNameAttribute = FirstNonEmpty(request.GroupNameAttribute, row.GroupNameAttribute);
            row.GroupIdAttribute = FirstNonEmpty(request.GroupIdAttribute, row.GroupIdAttribute);
            row.MembershipAttribute = FirstNonEmpty(request.MembershipAttribute, row.MembershipAttribute);
            row.ProvisionOnLogin = request.ProvisionOnLogin;
            row.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(request.BindPassword))
            {
                row.BindPasswordProtected = _secrets.Protect(request.BindPassword);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        public async Task TestAsync(DirectoryWriteRequest? request, CancellationToken cancellationToken)
        {
            DirectoryConnection row = await RowAsync(cancellationToken);
            DirectoryWriteRequest probe = request ?? new DirectoryWriteRequest();
            string? error = Validate(probe, passwordAlreadyStored: !string.IsNullOrEmpty(row.BindPasswordProtected) || !string.IsNullOrEmpty(probe.BindPassword), forceComplete: true);
            if (error != null)
            {
                throw new DirectoryFailureException(error);
            }

            string? overridePassword = string.IsNullOrEmpty(probe.BindPassword) ? null : probe.BindPassword;
            DirectoryConnection overlay = Overlay(row, probe);
            await _directory.TestAsync(SettingsFrom(overlay, overridePassword), cancellationToken);
        }

        public async Task<IReadOnlyList<DirectoryAccount>> SearchUsersAsync(string? query, CancellationToken cancellationToken)
        {
            string text = (query ?? "").Trim();
            if (text.Length < 1 || text.Length > 100)
            {
                throw new DirectoryFailureException("Enter between 1 and 100 characters to search for users");
            }

            DirectorySettings settings = await ConfiguredSettingsAsync(cancellationToken);
            return await _directory.SearchUsersAsync(settings, text, cancellationToken);
        }

        public async Task<IReadOnlyList<DirectoryGroupRecord>> SearchGroupsAsync(string? query, CancellationToken cancellationToken)
        {
            string text = (query ?? "").Trim();
            if (text.Length < 1 || text.Length > 100)
            {
                throw new DirectoryFailureException("Enter between 1 and 100 characters to search for groups");
            }

            DirectorySettings settings = await ConfiguredSettingsAsync(cancellationToken);
            return await _directory.SearchGroupsAsync(settings, text, cancellationToken);
        }

        public async Task<DirectoryImportResult> ImportUsersAsync(IReadOnlyList<string>? distinguishedNames, CancellationToken cancellationToken)
        {
            List<string> names = CleanNames(distinguishedNames);
            DirectorySettings settings = await ConfiguredSettingsAsync(cancellationToken);
            DirectoryImportResult result = new();
            foreach (string dn in names)
            {
                DirectoryAccount? account = await _directory.ReadUserAsync(settings, dn, cancellationToken);
                if (account == null)
                {
                    result.Errors.Add(new { distinguishedName = dn, error = "The directory entry was not found" });
                    continue;
                }

                (AppUser? user, string? error) = await UpsertUserAsync(account, createIfMissing: true, cancellationToken);
                if (user == null)
                {
                    result.Errors.Add(new { distinguishedName = dn, error });
                    continue;
                }

                await SyncGroupsAsync(user, account.GroupDistinguishedNames, cancellationToken);
                result.Imported.Add(new { id = user.Id, username = user.Username, distinguishedName = account.DistinguishedName });
            }

            return result;
        }

        public async Task<DirectoryImportResult> ImportGroupsAsync(
            IReadOnlyList<string>? distinguishedNames,
            bool canRead,
            bool canWrite,
            bool canAdmin,
            bool canReadWarehouse,
            bool canWriteWarehouse,
            CancellationToken cancellationToken)
        {
            if (canWrite)
            {
                canRead = true;
            }

            if (canWriteWarehouse)
            {
                canReadWarehouse = true;
            }

            List<string> names = CleanNames(distinguishedNames);
            DirectorySettings settings = await ConfiguredSettingsAsync(cancellationToken);
            DirectoryImportResult result = new();
            foreach (string dn in names)
            {
                DirectoryGroupRecord? record = await _directory.ReadGroupAsync(settings, dn, cancellationToken);
                if (record == null)
                {
                    result.Errors.Add(new { distinguishedName = dn, error = "The directory entry was not found" });
                    continue;
                }

                (AppGroup? group, string? error) = await UpsertGroupAsync(
                    record,
                    canRead,
                    canWrite,
                    canAdmin,
                    canReadWarehouse,
                    canWriteWarehouse,
                    cancellationToken);
                if (group == null)
                {
                    result.Errors.Add(new { distinguishedName = dn, error });
                    continue;
                }

                result.Imported.Add(new
                {
                    id = group.Id,
                    name = group.Name,
                    distinguishedName = record.DistinguishedName
                });
            }

            return result;
        }

        private async Task<(AppUser? User, string? Error)> UpsertUserAsync(
            DirectoryAccount account,
            bool createIfMissing,
            CancellationToken cancellationToken)
        {
            string directoryId = account.Id.Trim().ToLowerInvariant();
            string username = account.Username.Trim();
            if (directoryId.Length < 1 || directoryId.Length > 200 || username.Length < 1 || username.Length > 100)
            {
                return (null, "The directory account cannot be stored");
            }

            AppUser? user = await _db.AppUsers.FirstOrDefaultAsync(
                u => u.DirectoryId != null && u.DirectoryId.ToLower() == directoryId,
                cancellationToken);
            if (user == null)
            {
                AppUser? byName = await _db.AppUsers.FirstOrDefaultAsync(
                    u => u.Username.ToLower() == username.ToLower(),
                    cancellationToken);
                if (byName != null)
                {
                    if (byName.Source != "directory"
                        || (!string.IsNullOrEmpty(byName.DirectoryId)
                            && !string.Equals(byName.DirectoryId, directoryId, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (null, "A user with this name already exists");
                    }

                    user = byName;
                }
            }

            if (user == null)
            {
                if (!createIfMissing)
                {
                    return (null, "This directory account has not been imported");
                }

                DateTime now = DateTime.UtcNow;
                user = new AppUser
                {
                    Username = username,
                    PasswordHash = "",
                    IsActive = true,
                    Source = "directory",
                    DirectoryId = directoryId,
                    DisplayName = account.DisplayName,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.AppUsers.Add(user);
            }
            else
            {
                if (user.Source != "directory")
                {
                    return (null, "A local user already uses this name");
                }

                bool nameTaken = await _db.AppUsers.AnyAsync(
                    u => u.Id != user.Id && u.Username.ToLower() == username.ToLower(),
                    cancellationToken);
                if (nameTaken)
                {
                    return (null, "A user with this name already exists");
                }

                user.Username = username;
                user.Source = "directory";
                user.DirectoryId = directoryId;
                user.DisplayName = account.DisplayName;
                user.PasswordHash = "";
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(user).State = EntityState.Detached;
                return (null, "The directory account could not be stored");
            }

            return (user, null);
        }

        private async Task<(AppGroup? Group, string? Error)> UpsertGroupAsync(
            DirectoryGroupRecord record,
            bool canRead,
            bool canWrite,
            bool canAdmin,
            bool canReadWarehouse,
            bool canWriteWarehouse,
            CancellationToken cancellationToken)
        {
            string directoryId = record.Id.Trim().ToLowerInvariant();
            string name = record.Name.Trim();
            if (directoryId.Length < 1 || directoryId.Length > 200 || name.Length < 1 || name.Length > 100)
            {
                return (null, "The directory group cannot be stored");
            }

            AppGroup? group = await _db.AppGroups.FirstOrDefaultAsync(
                g => g.DirectoryId != null && g.DirectoryId.ToLower() == directoryId,
                cancellationToken);
            if (group == null && !string.IsNullOrWhiteSpace(record.DistinguishedName))
            {
                string dn = record.DistinguishedName.Trim().ToLowerInvariant();
                group = await _db.AppGroups.FirstOrDefaultAsync(
                    g => g.DirectoryDn != null && g.DirectoryDn.ToLower() == dn,
                    cancellationToken);
            }

            if (group == null)
            {
                AppGroup? byName = await _db.AppGroups.FirstOrDefaultAsync(
                    g => g.Name.ToLower() == name.ToLower(),
                    cancellationToken);
                if (byName != null)
                {
                    if (!string.IsNullOrEmpty(byName.DirectoryId)
                        && !string.Equals(byName.DirectoryId, directoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        return (null, "A group with this name is already linked to another directory group");
                    }

                    group = byName;
                }
            }

            if (group == null)
            {
                DateTime now = DateTime.UtcNow;
                group = new AppGroup
                {
                    Name = name,
                    Description = record.Description,
                    CanRead = canRead,
                    CanWrite = canWrite,
                    CanAdmin = canAdmin,
                    CanReadWarehouse = canReadWarehouse,
                    CanWriteWarehouse = canWriteWarehouse,
                    Source = "directory",
                    DirectoryId = directoryId,
                    DirectoryDn = record.DistinguishedName,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.AppGroups.Add(group);
            }
            else
            {
                if (!group.IsSystem)
                {
                    group.Name = name;
                }

                group.Source = "directory";
                group.DirectoryId = directoryId;
                group.DirectoryDn = record.DistinguishedName;
                if (string.IsNullOrWhiteSpace(group.Description) && !string.IsNullOrWhiteSpace(record.Description))
                {
                    group.Description = record.Description;
                }
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(group).State = EntityState.Detached;
                return (null, "The directory group could not be stored");
            }

            return (group, null);
        }

        private async Task SyncGroupsAsync(AppUser user, IReadOnlyList<string> groupDns, CancellationToken cancellationToken)
        {
            HashSet<string> wantedDns = groupDns
                .Where(dn => !string.IsNullOrWhiteSpace(dn))
                .Select(DirectoryFilters.NormalizeDn)
                .ToHashSet();
            List<AppGroup> linked = await _db.AppGroups
                .Where(g => g.DirectoryDn != null && g.DirectoryDn != "")
                .ToListAsync(cancellationToken);
            HashSet<int> wantedIds = linked
                .Where(g => g.DirectoryDn != null && wantedDns.Contains(DirectoryFilters.NormalizeDn(g.DirectoryDn)))
                .Select(g => g.Id)
                .ToHashSet();
            List<UserGroupLink> links = await _db.UserGroupLinks.Where(l => l.UserId == user.Id).ToListAsync(cancellationToken);
            foreach (UserGroupLink link in links.Where(l => l.Origin == "directory" && !wantedIds.Contains(l.GroupId)).ToList())
            {
                _db.UserGroupLinks.Remove(link);
            }

            foreach (int groupId in wantedIds)
            {
                UserGroupLink? link = links.FirstOrDefault(l => l.GroupId == groupId);
                if (link == null)
                {
                    _db.UserGroupLinks.Add(new UserGroupLink
                    {
                        UserId = user.Id,
                        GroupId = groupId,
                        Origin = "directory"
                    });
                }
                else
                {
                    link.Origin = "directory";
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<DirectorySettings> ConfiguredSettingsAsync(CancellationToken cancellationToken)
        {
            DirectoryConnection row = await RowAsync(cancellationToken);
            string? error = Validate(WriteFrom(row), passwordAlreadyStored: !string.IsNullOrEmpty(row.BindPasswordProtected), forceComplete: true);
            if (error != null)
            {
                throw new DirectoryFailureException(error);
            }

            return SettingsFrom(row);
        }

        private DirectorySettings SettingsFrom(DirectoryConnection row, string? passwordOverride = null)
        {
            string password = passwordOverride ?? "";
            if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(row.BindPasswordProtected))
            {
                try
                {
                    password = _secrets.Unprotect(row.BindPasswordProtected);
                }
                catch (Exception exception)
                {
                    throw new DirectoryFailureException("The stored bind password cannot be decrypted. Enter it again.", exception);
                }
            }

            return new DirectorySettings
            {
                Host = row.Host,
                Port = row.Port,
                Encryption = row.Encryption,
                AllowUntrustedCertificate = row.AllowUntrustedCertificate,
                BindDn = row.BindDn,
                BindPassword = password,
                UserBaseDn = row.UserBaseDn,
                UserFilter = row.UserFilter,
                UsernameAttribute = row.UsernameAttribute,
                DisplayNameAttribute = row.DisplayNameAttribute,
                UserIdAttribute = row.UserIdAttribute,
                GroupBaseDn = row.GroupBaseDn,
                GroupFilter = row.GroupFilter,
                GroupNameAttribute = row.GroupNameAttribute,
                GroupIdAttribute = row.GroupIdAttribute,
                MembershipAttribute = row.MembershipAttribute
            };
        }

        private async Task<DirectoryConnection> RowAsync(CancellationToken cancellationToken)
        {
            DirectoryConnection? row = await _db.DirectoryConnections.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
            if (row != null)
            {
                return row;
            }

            row = new DirectoryConnection
            {
                Id = 1,
                UpdatedAt = DateTime.UtcNow
            };
            _db.DirectoryConnections.Add(row);
            await _db.SaveChangesAsync(cancellationToken);
            return row;
        }

        private static object ToPayload(DirectoryConnection row)
        {
            DirectoryWriteRequest probe = WriteFrom(row);
            bool configured = Validate(probe, passwordAlreadyStored: !string.IsNullOrEmpty(row.BindPasswordProtected), forceComplete: true) == null;
            return new
            {
                enabled = row.Enabled,
                serverType = row.ServerType,
                host = row.Host,
                port = row.Port,
                encryption = row.Encryption,
                allowUntrustedCertificate = row.AllowUntrustedCertificate,
                bindDn = row.BindDn,
                bindPasswordSet = !string.IsNullOrEmpty(row.BindPasswordProtected),
                userBaseDn = row.UserBaseDn,
                userFilter = row.UserFilter,
                usernameAttribute = row.UsernameAttribute,
                displayNameAttribute = row.DisplayNameAttribute,
                userIdAttribute = row.UserIdAttribute,
                groupBaseDn = row.GroupBaseDn,
                groupFilter = row.GroupFilter,
                groupNameAttribute = row.GroupNameAttribute,
                groupIdAttribute = row.GroupIdAttribute,
                membershipAttribute = row.MembershipAttribute,
                provisionOnLogin = row.ProvisionOnLogin,
                configured
            };
        }

        private static DirectoryWriteRequest WriteFrom(DirectoryConnection row)
        {
            return new DirectoryWriteRequest
            {
                Enabled = row.Enabled,
                ServerType = row.ServerType,
                Host = row.Host,
                Port = row.Port,
                Encryption = row.Encryption,
                AllowUntrustedCertificate = row.AllowUntrustedCertificate,
                BindDn = row.BindDn,
                UserBaseDn = row.UserBaseDn,
                UserFilter = row.UserFilter,
                UsernameAttribute = row.UsernameAttribute,
                DisplayNameAttribute = row.DisplayNameAttribute,
                UserIdAttribute = row.UserIdAttribute,
                GroupBaseDn = row.GroupBaseDn,
                GroupFilter = row.GroupFilter,
                GroupNameAttribute = row.GroupNameAttribute,
                GroupIdAttribute = row.GroupIdAttribute,
                MembershipAttribute = row.MembershipAttribute,
                ProvisionOnLogin = row.ProvisionOnLogin
            };
        }

        private static DirectoryConnection Overlay(DirectoryConnection stored, DirectoryWriteRequest request)
        {
            return new DirectoryConnection
            {
                Id = stored.Id,
                Enabled = true,
                ServerType = (request.ServerType ?? stored.ServerType).Trim().ToLowerInvariant(),
                Host = (request.Host ?? "").Trim(),
                Port = request.Port,
                Encryption = (request.Encryption ?? "none").Trim().ToLowerInvariant(),
                AllowUntrustedCertificate = request.AllowUntrustedCertificate,
                BindDn = (request.BindDn ?? "").Trim(),
                BindPasswordProtected = stored.BindPasswordProtected,
                UserBaseDn = (request.UserBaseDn ?? "").Trim(),
                UserFilter = FirstNonEmpty(request.UserFilter, stored.UserFilter),
                UsernameAttribute = FirstNonEmpty(request.UsernameAttribute, stored.UsernameAttribute),
                DisplayNameAttribute = FirstNonEmpty(request.DisplayNameAttribute, stored.DisplayNameAttribute),
                UserIdAttribute = FirstNonEmpty(request.UserIdAttribute, stored.UserIdAttribute),
                GroupBaseDn = (request.GroupBaseDn ?? "").Trim(),
                GroupFilter = FirstNonEmpty(request.GroupFilter, stored.GroupFilter),
                GroupNameAttribute = FirstNonEmpty(request.GroupNameAttribute, stored.GroupNameAttribute),
                GroupIdAttribute = FirstNonEmpty(request.GroupIdAttribute, stored.GroupIdAttribute),
                MembershipAttribute = FirstNonEmpty(request.MembershipAttribute, stored.MembershipAttribute),
                ProvisionOnLogin = request.ProvisionOnLogin
            };
        }

        private static List<string> CleanNames(IReadOnlyList<string>? distinguishedNames)
        {
            if (distinguishedNames == null || distinguishedNames.Count == 0)
            {
                throw new DirectoryFailureException("Select at least one directory entry");
            }

            List<string> names = distinguishedNames
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
            if (names.Count == 0)
            {
                throw new DirectoryFailureException("Select at least one directory entry");
            }

            if (names.Any(name => name.Length > 1000))
            {
                throw new DirectoryFailureException("A distinguished name is too long");
            }

            return names;
        }

        private static string? Validate(DirectoryWriteRequest request, bool passwordAlreadyStored, bool forceComplete = false)
        {
            string serverType = (request.ServerType ?? "ldap").Trim().ToLowerInvariant();
            if (serverType is not ("ldap" or "ad"))
            {
                return "Server type must be ldap or ad";
            }

            string encryption = (request.Encryption ?? "none").Trim().ToLowerInvariant();
            if (encryption is not ("none" or "ssl" or "starttls"))
            {
                return "Encryption must be none, ssl, or starttls";
            }

            if (request.Port is < 1 or > 65535)
            {
                return "Port must be between 1 and 65535";
            }

            string? shape = CheckText("Host", request.Host, 255)
                ?? CheckText("Bind DN", request.BindDn, 1000)
                ?? CheckText("User search base", request.UserBaseDn, 1000)
                ?? CheckText("Group search base", request.GroupBaseDn, 1000)
                ?? CheckFilter("User filter", request.UserFilter, requireUsernameToken: true)
                ?? CheckFilter("Group filter", request.GroupFilter, requireUsernameToken: false)
                ?? CheckAttribute("Username attribute", request.UsernameAttribute)
                ?? CheckAttribute("Display name attribute", request.DisplayNameAttribute)
                ?? CheckAttribute("User id attribute", request.UserIdAttribute)
                ?? CheckAttribute("Group name attribute", request.GroupNameAttribute)
                ?? CheckAttribute("Group id attribute", request.GroupIdAttribute)
                ?? CheckAttribute("Membership attribute", request.MembershipAttribute);
            if (shape != null)
            {
                return shape;
            }

            bool complete = forceComplete || request.Enabled;
            if (!complete)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Host))
            {
                return "Host is required";
            }

            if (string.IsNullOrWhiteSpace(request.UserBaseDn) || string.IsNullOrWhiteSpace(request.GroupBaseDn))
            {
                return "User and group search bases are required";
            }

            if (string.IsNullOrWhiteSpace(request.UserFilter) || !request.UserFilter.Contains("{username}", StringComparison.Ordinal))
            {
                return "User filter must contain {username}";
            }

            if (string.IsNullOrWhiteSpace(request.GroupFilter))
            {
                return "Group filter is required";
            }

            foreach (string? attribute in new[]
                     {
                         request.UsernameAttribute,
                         request.DisplayNameAttribute,
                         request.UserIdAttribute,
                         request.GroupNameAttribute,
                         request.GroupIdAttribute,
                         request.MembershipAttribute
                     })
            {
                if (string.IsNullOrWhiteSpace(attribute))
                {
                    return "Directory attribute names are required";
                }
            }

            if (!string.IsNullOrWhiteSpace(request.BindDn) && string.IsNullOrEmpty(request.BindPassword) && !passwordAlreadyStored)
            {
                return "A bind password is required";
            }

            return null;
        }

        private static string? CheckText(string label, string? value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.Trim().Length > max)
            {
                return $"{label} must be at most {max} characters";
            }

            return null;
        }

        private static string? CheckFilter(string label, string? value, bool requireUsernameToken)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string filter = value.Trim();
            if (filter.Length > 500 || !filter.StartsWith('(') || !filter.EndsWith(')'))
            {
                return $"{label} must be an LDAP filter in parentheses, at most 500 characters";
            }

            if (requireUsernameToken && !filter.Contains("{username}", StringComparison.Ordinal))
            {
                return "User filter must contain {username}";
            }

            return null;
        }

        private static string? CheckAttribute(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!AttributeName.IsMatch(value.Trim()))
            {
                return $"{label} must be an LDAP attribute name";
            }

            return null;
        }

        private static string FirstNonEmpty(string? incoming, string current)
        {
            string value = (incoming ?? "").Trim();
            return value.Length == 0 ? current : value;
        }
    }
}
