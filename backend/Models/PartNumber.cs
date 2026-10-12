using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("part_number", Schema = "public")]
    public class PartNumber
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; }

        [Column("part_id")]
        [ForeignKey("Part")]
        public int? PartId { get; set; }

        [Column("manufacturer_id")]
        [ForeignKey("Manufacturer")]
        public int? ManufacturerId { get; set; }

        [MaxLength(100)]
        [Column("manufacturer_part_number")]
        public string? ManufacturerPartNumber { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; } = 1;

        [MaxLength(50)]
        [Column("package_type")]
        public string? PackageType { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime? UpdatedAt { get; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "timestamp with time zone")]
        public DateTime? CreatedAt { get; }

        // Navigation properties
        public Part? Part { get; set; }

        public Manufacturer? Manufacturer { get; set; }
    }
}
