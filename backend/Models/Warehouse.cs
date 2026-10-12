using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("warehouse_item", Schema = "public")]
    public class WarehouseItem
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("uuid")]
        public Guid Uuid { get; set; }

        [Column("part_id")]
        public int? PartId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("value")]
        public string Value { get; set; } = "";

        [Column("comment")]
        public string Comment { get; set; } = "";

        /// <summary>SHA-256 of a photo for a manual entry. Catalog rows use the part photo instead.</summary>
        [MaxLength(64)]
        [Column("image_hash", TypeName = "character(64)")]
        public string? ImageHash { get; set; }

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }

        public Part? Part { get; set; }

        public List<WarehouseBatch> Batches { get; set; } = new();
    }

    [Table("warehouse_batch", Schema = "public")]
    public class WarehouseBatch
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("item_id")]
        public int ItemId { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("location")]
        public string Location { get; set; } = "";

        [Column("created_at", TypeName = "timestamptz")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at", TypeName = "timestamptz")]
        public DateTime UpdatedAt { get; set; }

        public WarehouseItem? Item { get; set; }
    }
}
