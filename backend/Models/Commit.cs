using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("commit", Schema = "public")]
    public class Commit
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; set; }

        [Column("parent_commit_id")]
        public int? ParentCommitId { get; set; }

        [Column("message", TypeName = "text")]
        public required string Message { get; set; }

        [Column("author", TypeName = "text")]
        public required string Author { get; set; }

        [Column("client", TypeName = "text")]
        public string? Client { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }

        [ForeignKey(nameof(ParentCommitId))]
        public Commit? ParentCommit { get; set; }
    }
}
