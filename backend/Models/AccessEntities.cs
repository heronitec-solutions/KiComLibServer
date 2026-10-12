using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("app_user", Schema = "public")]
    public class AppUser
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("username")]
        public string Username { get; set; } = "";

        [Column("password_hash")]
        public string PasswordHash { get; set; } = "";

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>local or directory.</summary>
        [Column("source")]
        public string Source { get; set; } = "local";

        [Column("directory_id")]
        public string? DirectoryId { get; set; }

        [Column("display_name")]
        public string DisplayName { get; set; } = "";

        public List<AppGroup> Groups { get; set; } = new();
    }

    [Table("app_group", Schema = "public")]
    public class AppGroup
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("description")]
        public string Description { get; set; } = "";

        [Column("can_read")]
        public bool CanRead { get; set; } = true;

        [Column("can_write")]
        public bool CanWrite { get; set; }

        [Column("can_admin")]
        public bool CanAdmin { get; set; }

        [Column("can_read_warehouse")]
        public bool CanReadWarehouse { get; set; }

        [Column("can_write_warehouse")]
        public bool CanWriteWarehouse { get; set; }

        [Column("is_system")]
        public bool IsSystem { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>local or directory.</summary>
        [Column("source")]
        public string Source { get; set; } = "local";

        [Column("directory_id")]
        public string? DirectoryId { get; set; }

        [Column("directory_dn")]
        public string? DirectoryDn { get; set; }

        public List<AppUser> Users { get; set; } = new();
    }

    /// <summary>
    /// Join row for <c>user_group</c>. <c>directory</c> membership is replaced from the
    /// directory at sign-in. <c>manual</c> membership is assigned in KiComLib and kept.
    /// </summary>
    public class UserGroupLink
    {
        public int UserId { get; set; }

        public int GroupId { get; set; }

        public string Origin { get; set; } = "manual";
    }

    [Table("api_token", Schema = "public")]
    public class ApiToken
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("token_hash")]
        public string TokenHash { get; set; } = "";

        [Column("token_prefix")]
        public string TokenPrefix { get; set; } = "";

        [Column("can_write")]
        public bool CanWrite { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("expires_at", TypeName = "timestamptz")]
        public DateTime ExpiresAt { get; set; }

        [Column("last_used_at", TypeName = "timestamptz")]
        public DateTime? LastUsedAt { get; set; }

        [Column("revoked_at", TypeName = "timestamptz")]
        public DateTime? RevokedAt { get; set; }

        public const int LifetimeDays = 90;

        public AppUser User { get; set; } = null!;
    }

    [Table("user_session", Schema = "public")]
    public class UserSession
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("token_hash")]
        public string TokenHash { get; set; } = "";

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("expires_at", TypeName = "timestamptz")]
        public DateTime ExpiresAt { get; set; }

        [Column("last_used_at", TypeName = "timestamptz")]
        public DateTime? LastUsedAt { get; set; }

        public AppUser User { get; set; } = null!;
    }

    [Table("app_setting", Schema = "public")]
    public class AppSetting
    {
        [Key]
        [Column("key")]
        public string Key { get; set; } = "";

        [Column("value")]
        public string Value { get; set; } = "";

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }
    }

    [Table("directory_connection", Schema = "public")]
    public class DirectoryConnection
    {
        [Key]
        [Column("id")]
        public int Id { get; set; } = 1;

        [Column("enabled")]
        public bool Enabled { get; set; }

        [Column("server_type")]
        public string ServerType { get; set; } = "ldap";

        [Column("host")]
        public string Host { get; set; } = "";

        [Column("port")]
        public int Port { get; set; } = 389;

        [Column("encryption")]
        public string Encryption { get; set; } = "none";

        [Column("allow_untrusted_certificate")]
        public bool AllowUntrustedCertificate { get; set; }

        [Column("bind_dn")]
        public string BindDn { get; set; } = "";

        [Column("bind_password_protected")]
        public string BindPasswordProtected { get; set; } = "";

        [Column("user_base_dn")]
        public string UserBaseDn { get; set; } = "";

        [Column("user_filter")]
        public string UserFilter { get; set; } = "(&(objectClass=inetOrgPerson)(uid={username}))";

        [Column("username_attribute")]
        public string UsernameAttribute { get; set; } = "uid";

        [Column("display_name_attribute")]
        public string DisplayNameAttribute { get; set; } = "displayName";

        [Column("user_id_attribute")]
        public string UserIdAttribute { get; set; } = "entryUUID";

        [Column("group_base_dn")]
        public string GroupBaseDn { get; set; } = "";

        [Column("group_filter")]
        public string GroupFilter { get; set; } = "(objectClass=groupOfNames)";

        [Column("group_name_attribute")]
        public string GroupNameAttribute { get; set; } = "cn";

        [Column("group_id_attribute")]
        public string GroupIdAttribute { get; set; } = "entryUUID";

        [Column("membership_attribute")]
        public string MembershipAttribute { get; set; } = "member";

        [Column("provision_on_login")]
        public bool ProvisionOnLogin { get; set; } = true;

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }
    }
}
