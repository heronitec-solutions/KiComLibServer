using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;

namespace KiComLibBackend.Models
{
    [Table("part", Schema = "public")]
    public class Part
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

        [Column("comment", TypeName = "text")]
        public string? Comment { get; set; }

        [Column("datasheet_id")]
        [ForeignKey("Datasheet")]
        public int? DatasheetId { get; set; }

        /// <summary>Every datasheet on this part, primary first. <see cref="DatasheetId"/> mirrors the first id.</summary>
        [Column("datasheet_ids", TypeName = "json")]
        public List<int> DatasheetIds { get; set; } = new();

        [MaxLength(100)]
        [Column("datasheet")]
        public string? Datasheet { get; set; }

        [Column("library_id")]
        [ForeignKey("Library")]
        public int? LibraryId { get; set; }

        [Column("symbol_id")]
        [ForeignKey("Symbol")]
        public int? SymbolId { get; set; }

        [Column("footprint_id")]
        [ForeignKey("Footprint")]
        public int? FootprintId { get; set; }

        private string _lifecycleStatus = "active";

        [MaxLength(16)]
        [Column("lifecycle_status")]
        public string LifecycleStatus
        {
            get => _lifecycleStatus;
            set => _lifecycleStatus = string.IsNullOrWhiteSpace(value) ? "active" : value;
        }

        [Column("keywords", TypeName = "text")]
        public string? Keywords { get; set; }

        [MaxLength(100)]
        [Column("value")]
        public string? Value { get; set; }

        [Column("properties", TypeName = "json")]
        public JObject? Properties { get; } //set; }

        [Column("alternatives", TypeName = "json")]
        public List<int> Alternatives { get; set; } = new();

        [Column("links", TypeName = "json")]
        public List<PartLink> Links { get; set; } = new();

        /// <summary>SHA-256 of the product photo in <c>blob</c>. Empty when the part has no photo.</summary>
        [MaxLength(64)]
        [Column("image_hash", TypeName = "character(64)")]
        public string? ImageHash { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime? UpdatedAt { get; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "timestamp with time zone")]
        public DateTime? CreatedAt { get; }

        // Navigation properties
        public Datasheet? DatasheetNavigation { get; set; }

        public Library? Library { get; set; }

        public Symbol? Symbol { get; set; }

        public Footprint? Footprint { get; set; }
    }

    public class PartLink
    {
        public string Label { get; set; } = "";

        public string Url { get; set; } = "";
    }
}
