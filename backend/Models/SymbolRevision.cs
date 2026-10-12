using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("symbol_revision", Schema = "public")]
    public class SymbolRevision
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [Column("symbol_id")]
        public int SymbolId { get; set; }

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

        [MaxLength(50)]
        [Column("reference")]
        public string? Reference { get; set; }

        [Column("in_bom")]
        public bool InBom { get; set; } = true;

        [Column("on_board")]
        public bool OnBoard { get; set; } = true;

        [Column("pin_numbers_hidden")]
        public bool PinNumbersHidden { get; set; }

        [Column("pin_names_hidden")]
        public bool PinNamesHidden { get; set; }

        [Column("pin_names_offset", TypeName = "real")]
        public float? PinNamesOffset { get; set; }

        [Column("description", TypeName = "text")]
        public string? Description { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(SymbolId))]
        public Symbol? Symbol { get; set; }

        [ForeignKey(nameof(CommitId))]
        public Commit? Commit { get; set; }
    }
}
