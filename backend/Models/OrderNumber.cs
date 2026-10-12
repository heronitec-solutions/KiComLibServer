using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("order_number", Schema = "public")]
    public class OrderNumber
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int? Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("uuid")]
        public Guid? Uuid { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("order_number")]
        public required string OrderNumberValue { get; set; }

        [Column("part_number_id")]
        [ForeignKey("PartNumber")]
        public int? PartNumberId { get; set; }

        [Column("distributor_id")]
        [ForeignKey("Distributor")]
        public int? DistributorId { get; set; }

        [Column("quantity_min")]
        public int QuantityMin { get; set; } = 1;

        [Column("quantity_steps")]
        public int QuantitySteps { get; set; } = 1;

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("updated_at", TypeName = "timestamp with time zone")]
        public DateTime? UpdatedAt { get; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("created_at", TypeName = "timestamp with time zone")]
        public DateTime? CreatedAt { get; }

        // Navigation properties
        public PartNumber? PartNumber { get; set; }

        public Distributor? Distributor { get; set; }
    }
}
