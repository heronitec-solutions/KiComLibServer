using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("git_host", Schema = "public")]
    public class GitHost
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("kind")]
        public string Kind { get; set; } = "";

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("web_url")]
        public string WebUrl { get; set; } = "";

        [Column("ssh_host")]
        public string? SshHost { get; set; }

        [Column("ca_certificate")]
        public string? CaCertificate { get; set; }

        [Column("ssh_known_hosts")]
        public string? SshKnownHosts { get; set; }

        [Column("allow_http")]
        public bool AllowHttp { get; set; }

        [Column("is_system")]
        public bool IsSystem { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public List<GitCredential> Credentials { get; set; } = new();

        public List<GitRepository> Repositories { get; set; } = new();
    }

    [Table("git_credential", Schema = "public")]
    public class GitCredential
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("host_id")]
        public int HostId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("kind")]
        public string Kind { get; set; } = "";

        [Column("username")]
        public string? Username { get; set; }

        [Column("secret_protected")]
        public string SecretProtected { get; set; } = "";

        [Column("secret_hint")]
        public string SecretHint { get; set; } = "";

        [Column("public_key")]
        public string? PublicKey { get; set; }

        [Column("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [Column("last_used_at")]
        public DateTime? LastUsedAt { get; set; }

        [Column("last_error")]
        public string? LastError { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public GitHost? Host { get; set; }
    }

    [Table("git_repository", Schema = "public")]
    public class GitRepository
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("host_id")]
        public int HostId { get; set; }

        [Column("credential_id")]
        public int? CredentialId { get; set; }

        [Column("clone_url")]
        public string CloneUrl { get; set; } = "";

        [Column("web_url")]
        public string? WebUrl { get; set; }

        [Column("provider_path")]
        public string ProviderPath { get; set; } = "";

        [Column("default_branch")]
        public string? DefaultBranch { get; set; }

        [Column("tracked_branches", TypeName = "text[]")]
        public string[] TrackedBranches { get; set; } = Array.Empty<string>();

        [Column("tag_pattern")]
        public string TagPattern { get; set; } = "*";

        [Column("sync_mode")]
        public string SyncMode { get; set; } = "poll";

        [Column("poll_interval_minutes")]
        public int? PollIntervalMinutes { get; set; }

        [Column("webhook_secret_protected")]
        public string? WebhookSecretProtected { get; set; }

        [Column("lfs_enabled")]
        public bool LfsEnabled { get; set; }

        [Column("status")]
        public string Status { get; set; } = "new";

        [Column("last_fetch_at")]
        public DateTime? LastFetchAt { get; set; }

        [Column("last_error")]
        public string? LastError { get; set; }

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public GitHost? Host { get; set; }

        public GitCredential? Credential { get; set; }

        public List<GitRef> Refs { get; set; } = new();

        public List<Project> Projects { get; set; } = new();
    }

    [Table("git_ref", Schema = "public")]
    public class GitRef
    {
        [Column("repository_id")]
        public int RepositoryId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("sha")]
        public string Sha { get; set; } = "";

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public GitRepository? Repository { get; set; }
    }
}
