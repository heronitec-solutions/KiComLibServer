using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace KiComLibBackend.Models
{
    // public enum LibraryFileType
    // {
    //     footprint = 0,
    //     symbol = 1
    // }


    [Table("library_file", Schema = "public")]
    public class LibraryFile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; }

        // [Required]
        // [Column("type")]
        // [JsonConverter(typeof(JsonNumberEnumConverter<LibraryFileType>))]
        // public LibraryFileType Type { get; set; } // Enum will be handled in OnModelCreating

        [Required]
        [Column("type")]
        public int Type { get; set; } // Enum will be handled in OnModelCreating

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public required string Name { get; set; }

        [Column("comment")]
        public string? Comment { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("filename")]
        public required string Filename { get; set; }

        [Column("description")]
        public string? Description { get; set; }

        [MaxLength(8)]
        [Column("version")]
        public string? Version { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime? UpdatedAt { get; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "timestamp with time zone")]
        public DateTime? CreatedAt { get; }

        public ICollection<Footprint>? Footprints { get; set; }
        public ICollection<Symbol>? Symbols { get; set; }
    }
}
