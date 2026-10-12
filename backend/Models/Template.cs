using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("template", Schema = "public")]
    public class Template
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; set; }

        [Column("directory", TypeName = "text")]
        public string Directory { get; set; } = "";

        [Required]
        [Column("filename", TypeName = "text")]
        public required string Filename { get; set; }

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

        [ForeignKey(nameof(HeadRevisionId))]
        public TemplateRevision? HeadRevision { get; set; }

        public ICollection<TemplateRevision> Revisions { get; set; } = new List<TemplateRevision>();
    }
}
