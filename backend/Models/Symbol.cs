using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("symbol", Schema = "public")]
    public class Symbol
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; set; }

        [Required]
        [ForeignKey(nameof(LibraryFile))]
        [Column("library_file_id")]
        public required int LibraryFileId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public required string Name { get; set; }

        [Column("head_revision_id")]
        public int? HeadRevisionId { get; set; }

        [Column("deleted_at", TypeName = "timestamptz")]
        public DateTime? DeletedAt { get; set; }

        [Column("updated_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime? UpdatedAt { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        public LibraryFile? LibraryFile { get; set; }

        [ForeignKey(nameof(HeadRevisionId))]
        public SymbolRevision? HeadRevision { get; set; }

        public ICollection<SymbolRevision> Revisions { get; set; } = new List<SymbolRevision>();
    }
}
