namespace KiComLibBackend.Services
{
    public class DirectoryWriteRequest
    {
        public bool Enabled { get; set; }

        public string? ServerType { get; set; }

        public string? Host { get; set; }

        public int Port { get; set; } = 389;

        public string? Encryption { get; set; }

        public bool AllowUntrustedCertificate { get; set; }

        public string? BindDn { get; set; }

        public string? BindPassword { get; set; }

        public string? UserBaseDn { get; set; }

        public string? UserFilter { get; set; }

        public string? UsernameAttribute { get; set; }

        public string? DisplayNameAttribute { get; set; }

        public string? UserIdAttribute { get; set; }

        public string? GroupBaseDn { get; set; }

        public string? GroupFilter { get; set; }

        public string? GroupNameAttribute { get; set; }

        public string? GroupIdAttribute { get; set; }

        public string? MembershipAttribute { get; set; }

        public bool ProvisionOnLogin { get; set; } = true;
    }

    public sealed class DirectorySettings
    {
        public string Host { get; init; } = "";

        public int Port { get; init; } = 389;

        public string Encryption { get; init; } = "none";

        public bool AllowUntrustedCertificate { get; init; }

        public string BindDn { get; init; } = "";

        public string BindPassword { get; init; } = "";

        public string UserBaseDn { get; init; } = "";

        public string UserFilter { get; init; } = "";

        public string UsernameAttribute { get; init; } = "";

        public string DisplayNameAttribute { get; init; } = "";

        public string UserIdAttribute { get; init; } = "";

        public string GroupBaseDn { get; init; } = "";

        public string GroupFilter { get; init; } = "";

        public string GroupNameAttribute { get; init; } = "";

        public string GroupIdAttribute { get; init; } = "";

        public string MembershipAttribute { get; init; } = "member";
    }

    public sealed class DirectoryAccount
    {
        public string Id { get; init; } = "";

        public string Username { get; init; } = "";

        public string DisplayName { get; init; } = "";

        public string DistinguishedName { get; init; } = "";

        public IReadOnlyList<string> GroupDistinguishedNames { get; init; } = Array.Empty<string>();
    }

    public sealed class DirectoryGroupRecord
    {
        public string Id { get; init; } = "";

        public string Name { get; init; } = "";

        public string DistinguishedName { get; init; } = "";

        public string Description { get; init; } = "";
    }

    public sealed class DirectoryFailureException : Exception
    {
        public DirectoryFailureException(string message) : base(message)
        {
        }

        public DirectoryFailureException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    public interface IDirectoryClient
    {
        Task TestAsync(DirectorySettings settings, CancellationToken cancellationToken);

        Task<DirectoryAccount?> AuthenticateAsync(DirectorySettings settings, string username, string password, CancellationToken cancellationToken);

        Task<IReadOnlyList<DirectoryAccount>> SearchUsersAsync(DirectorySettings settings, string query, CancellationToken cancellationToken);

        Task<IReadOnlyList<DirectoryGroupRecord>> SearchGroupsAsync(DirectorySettings settings, string query, CancellationToken cancellationToken);

        Task<DirectoryAccount?> ReadUserAsync(DirectorySettings settings, string distinguishedName, CancellationToken cancellationToken);

        Task<DirectoryGroupRecord?> ReadGroupAsync(DirectorySettings settings, string distinguishedName, CancellationToken cancellationToken);
    }
}
