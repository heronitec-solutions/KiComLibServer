using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("project", Schema = "public")]
    public class Project
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("repository_id")]
        public int RepositoryId { get; set; }

        [Column("project_file")]
        public string ProjectFile { get; set; } = "";

        [Column("export_scope")]
        public string ExportScope { get; set; } = "project_dir";

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("description")]
        public string? Description { get; set; }

        [Column("status")]
        public string Status { get; set; } = "development";

        [Column("customer")]
        public string? Customer { get; set; }

        [Column("owner_user_id")]
        public int? OwnerUserId { get; set; }

        [Column("head_revision_id")]
        public int? HeadRevisionId { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public GitRepository? Repository { get; set; }

        public ProjectRevision? HeadRevision { get; set; }

        public List<ProjectRevision> Revisions { get; set; } = new();

        public List<ProjectGroupAccess> GroupAccess { get; set; } = new();
    }

    [Table("project_group_access", Schema = "public")]
    public class ProjectGroupAccess
    {
        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("group_id")]
        public int GroupId { get; set; }

        [Column("can_write")]
        public bool CanWrite { get; set; }

        public Project? Project { get; set; }

        public AppGroup? Group { get; set; }
    }

    [Table("project_blob", Schema = "public")]
    public class ProjectBlob
    {
        [Key]
        [Column("hash")]
        public string Hash { get; set; } = "";

        [Column("size")]
        public long Size { get; set; }

        [Column("media_type")]
        public string MediaType { get; set; } = "";

        [Column("data")]
        public byte[]? Data { get; set; }

        [Column("storage")]
        public short Storage { get; set; }

        [Column("external_ref")]
        public string? ExternalRef { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    [Table("project_revision", Schema = "public")]
    public class ProjectRevision
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("commit_sha")]
        public string CommitSha { get; set; } = "";

        [Column("parent_shas", TypeName = "text[]")]
        public string[] ParentShas { get; set; } = Array.Empty<string>();

        [Column("committed_at")]
        public DateTime CommittedAt { get; set; }

        [Column("author_name")]
        public string AuthorName { get; set; } = "";

        [Column("author_email")]
        public string? AuthorEmail { get; set; }

        [Column("message")]
        public string Message { get; set; } = "";

        [Column("refs", TypeName = "text[]")]
        public string[] Refs { get; set; } = Array.Empty<string>();

        [Column("reachable")]
        public bool Reachable { get; set; } = true;

        [Column("kicad_version")]
        public string? KicadVersion { get; set; }

        [Column("analysis_status")]
        public string AnalysisStatus { get; set; } = "pending";

        [Column("analysis_error")]
        public string? AnalysisError { get; set; }

        [Column("netlist_blob_hash")]
        public string? NetlistBlobHash { get; set; }

        [Column("catalog_commit_id")]
        public int? CatalogCommitId { get; set; }

        [Column("analyzed_at")]
        public DateTime? AnalyzedAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        public Project? Project { get; set; }
    }

    [Table("project_component", Schema = "public")]
    public class ProjectComponent
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("revision_id")]
        public int RevisionId { get; set; }

        [Column("variant")]
        public string Variant { get; set; } = "";

        [Column("symbol_path")]
        public string SymbolPath { get; set; } = "";

        [Column("symbol_uuids", TypeName = "uuid[]")]
        public Guid[] SymbolUuids { get; set; } = Array.Empty<Guid>();

        [Column("sheet_names")]
        public string SheetNames { get; set; } = "/";

        [Column("reference")]
        public string Reference { get; set; } = "";

        [Column("value")]
        public string? Value { get; set; }

        [Column("footprint")]
        public string? Footprint { get; set; }

        [Column("lib_id")]
        public string? LibId { get; set; }

        [Column("kicomlib_uuid")]
        public Guid? KicomlibUuid { get; set; }

        [Column("mpn")]
        public string? Mpn { get; set; }

        [Column("manufacturer")]
        public string? Manufacturer { get; set; }

        [Column("fields", TypeName = "jsonb")]
        public string Fields { get; set; } = "{}";

        [Column("dnp")]
        public bool Dnp { get; set; }

        [Column("exclude_from_bom")]
        public bool ExcludeFromBom { get; set; }

        [Column("exclude_from_board")]
        public bool ExcludeFromBoard { get; set; }

        [Column("part_id")]
        public int? PartId { get; set; }

        [Column("match_method")]
        public string MatchMethod { get; set; } = "none";
    }

    [Table("project_release", Schema = "public")]
    public class ProjectRelease
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("revision_id")]
        public int RevisionId { get; set; }

        [Column("tag")]
        public string? Tag { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("notes")]
        public string? Notes { get; set; }

        [Column("state")]
        public string State { get; set; } = "released";

        [Column("released_by")]
        public int? ReleasedBy { get; set; }

        [Column("released_at")]
        public DateTime ReleasedAt { get; set; }
    }

    [Table("project_artifact", Schema = "public")]
    public class ProjectArtifact
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("revision_id")]
        public int RevisionId { get; set; }

        [Column("kind")]
        public string Kind { get; set; } = "";

        [Column("variant")]
        public string Variant { get; set; } = "";

        [Column("file_name")]
        public string FileName { get; set; } = "";

        [Column("blob_hash")]
        public string BlobHash { get; set; } = "";

        [Column("kicad_version")]
        public string KicadVersion { get; set; } = "";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    [Table("project_diff", Schema = "public")]
    public class ProjectDiff
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("base_revision_id")]
        public int BaseRevisionId { get; set; }

        [Column("target_revision_id")]
        public int TargetRevisionId { get; set; }

        [Column("status")]
        public string Status { get; set; } = "queued";

        [Column("summary", TypeName = "jsonb")]
        public string? Summary { get; set; }

        [Column("kicad_version")]
        public string? KicadVersion { get; set; }

        [Column("error")]
        public string? Error { get; set; }

        [Column("last_viewed_at")]
        public DateTime? LastViewedAt { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    [Table("project_job", Schema = "public")]
    public class ProjectJob
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("kind")]
        public string Kind { get; set; } = "";

        [Column("dedup_key")]
        public string? DedupKey { get; set; }

        [Column("repository_id")]
        public int? RepositoryId { get; set; }

        [Column("project_id")]
        public int? ProjectId { get; set; }

        [Column("payload", TypeName = "jsonb")]
        public string Payload { get; set; } = "{}";

        [Column("status")]
        public string Status { get; set; } = "queued";

        [Column("attempts")]
        public int Attempts { get; set; }

        [Column("run_after")]
        public DateTime RunAfter { get; set; }

        [Column("started_at")]
        public DateTime? StartedAt { get; set; }

        [Column("finished_at")]
        public DateTime? FinishedAt { get; set; }

        [Column("error")]
        public string? Error { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
