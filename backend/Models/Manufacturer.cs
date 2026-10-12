using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("manufacturer", Schema = "public")]
    public class Manufacturer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public required string Name { get; set; }

        [MaxLength(200)]
        [Column("url")]
        public string? Url { get; set; }

        [MaxLength(100)]
        [Column("trustedparts_id")]
        public string? TrustedPartsId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime? UpdatedAt { get; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "timestamp with time zone")]
        public DateTime? CreatedAt { get; }
    }
}
