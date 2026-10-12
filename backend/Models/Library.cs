using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{

    [Table("library", Schema = "public")]
    public class Library
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

        [Required]
        [MaxLength(100)]
        [Column("filename")]
        public required string Filename { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [Column("special_columns", TypeName = "text[]")]
        public string[]? SpecialColumns { get; set; }

        [Column("updated_at", TypeName = "timestamp with time zone")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public DateTime? UpdatedAt { get; }

        [Column("created_at", TypeName = "timestamp with time zone")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime? CreatedAt { get; }
    }
}
