using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("schema_version", Schema = "public")]
    public class SchemaVersion
    {
        [Key]
        [Column("version")]
        public int Version { get; set; }

        [Column("applied_at", TypeName = "timestamptz")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? AppliedAt { get; set; }
    }
}
