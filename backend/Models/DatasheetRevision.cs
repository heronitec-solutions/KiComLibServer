using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("datasheet_revision", Schema = "public")]
    public class DatasheetRevision
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [Column("datasheet_id")]
        public int DatasheetId { get; set; }

        [Column("commit_id")]
        public int CommitId { get; set; }

        [Column("revision_no")]
        public int RevisionNo { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("directory", TypeName = "text")]
        public string Directory { get; set; } = "";

        [Column("filename", TypeName = "text")]
        public required string Filename { get; set; }

        [Column("comment", TypeName = "text")]
        public string? Comment { get; set; }

        [Column("blob_hash", TypeName = "character(64)")]
        public string? BlobHash { get; set; }

        [Column("size")]
        public long? Size { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(DatasheetId))]
        public Datasheet? Datasheet { get; set; }

        [ForeignKey(nameof(CommitId))]
        public Commit? Commit { get; set; }
    }
}
