using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("blob", Schema = "public")]
    public class Blob
    {
        [Key]
        [Column("hash", TypeName = "character(64)")]
        [MaxLength(64)]
        public required string Hash { get; set; }

        [Column("size")]
        public long Size { get; set; }

        [Column("media_type", TypeName = "text")]
        public required string MediaType { get; set; }

        [Column("data", TypeName = "bytea")]
        public byte[]? Data { get; set; }

        [Column("storage")]
        public short Storage { get; set; } = 0;

        [Column("external_ref", TypeName = "text")]
        public string? ExternalRef { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; set; }
    }
}
