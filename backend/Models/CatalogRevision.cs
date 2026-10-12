using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("catalog_revision", Schema = "public")]
    public class CatalogRevision
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [Column("entity")]
        public required string Entity { get; set; }

        [Column("entity_id")]
        public int EntityId { get; set; }

        [Column("entity_uuid")]
        public Guid EntityUuid { get; set; }

        [Column("commit_id")]
        public int CommitId { get; set; }

        [Column("revision_no")]
        public int RevisionNo { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("snapshot", TypeName = "text")]
        public string Snapshot { get; set; } = "{}";

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(CommitId))]
        public Commit? Commit { get; set; }
    }
}
