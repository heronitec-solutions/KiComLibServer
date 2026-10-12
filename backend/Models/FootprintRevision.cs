using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("footprint_revision", Schema = "public")]
    public class FootprintRevision
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [Column("footprint_id")]
        public int FootprintId { get; set; }

        [Column("commit_id")]
        public int CommitId { get; set; }

        [Column("revision_no")]
        public int RevisionNo { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("library_file_id")]
        public int LibraryFileId { get; set; }

        [MaxLength(100)]
        [Column("name")]
        public required string Name { get; set; }

        [Column("blob_hash", TypeName = "character(64)")]
        public string? BlobHash { get; set; }

        [Column("preview_blob_hash", TypeName = "character(64)")]
        public string? PreviewBlobHash { get; set; }

        [Column("type")]
        public int Type { get; set; }

        [Column("board_only")]
        public bool BoardOnly { get; set; }

        [Column("exclude_from_pos_files")]
        public bool ExcludeFromPosFiles { get; set; }

        [Column("exclude_from_bom")]
        public bool ExcludeFromBom { get; set; }

        [Column("height", TypeName = "real")]
        public float Height { get; set; }

        [MaxLength(10)]
        [Column("layer")]
        public required string Layer { get; set; }

        [Column("description", TypeName = "text")]
        public string? Description { get; set; }

        [Column("tags", TypeName = "text")]
        public string? Tags { get; set; }

        [Column("property", TypeName = "text")]
        public string? Property { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(FootprintId))]
        public Footprint? Footprint { get; set; }

        [ForeignKey(nameof(CommitId))]
        public Commit? Commit { get; set; }

        public ICollection<FootprintRevisionModel3D> Models { get; set; } = new List<FootprintRevisionModel3D>();
    }
}
