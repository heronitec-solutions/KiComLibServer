using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace KiComLibBackend.Models
{
    public class KiCadHttpValidationData
    {
        public required string Categories { get; set; }

        public required string Parts { get; set; }
    }
}
