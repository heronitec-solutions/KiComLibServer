using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    [Table("footprint_revision_model3d", Schema = "public")]
    public class FootprintRevisionModel3D
    {
        [Column("footprint_revision_id")]
        public int FootprintRevisionId { get; set; }

        [Column("position")]
        public short Position { get; set; }

        [Column("model3d_id")]
        public int Model3dId { get; set; }

        [Column("offset_x", TypeName = "real")]
        public float OffsetX { get; set; }

        [Column("offset_y", TypeName = "real")]
        public float OffsetY { get; set; }

        [Column("offset_z", TypeName = "real")]
        public float OffsetZ { get; set; }

        [Column("scale_x", TypeName = "real")]
        public float ScaleX { get; set; } = 1;

        [Column("scale_y", TypeName = "real")]
        public float ScaleY { get; set; } = 1;

        [Column("scale_z", TypeName = "real")]
        public float ScaleZ { get; set; } = 1;

        [Column("rotate_x", TypeName = "real")]
        public float RotateX { get; set; }

        [Column("rotate_y", TypeName = "real")]
        public float RotateY { get; set; }

        [Column("rotate_z", TypeName = "real")]
        public float RotateZ { get; set; }

        [Column("opacity", TypeName = "real")]
        public float? Opacity { get; set; }

        [Column("hide")]
        public bool Hide { get; set; }

        [ForeignKey(nameof(FootprintRevisionId))]
        public FootprintRevision? FootprintRevision { get; set; }

        [ForeignKey(nameof(Model3dId))]
        public Model3D? Model3D { get; set; }
    }
}
