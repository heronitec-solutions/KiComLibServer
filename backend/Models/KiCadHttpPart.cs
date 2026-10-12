using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace KiComLibBackend.Models
{
    public class FieldDetail
    {
        [JsonProperty("value")]
        public string? Value { get; set; }

        // "visible" is optional, so it's nullable.
        [JsonProperty("visible")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? Visible { get; set; }
    }


    public class KiCadHttpPart
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("symbolIdStr")]
        public string? symbolIdStr { get; set; }

        [JsonProperty("exclude_from_bom")]
        public string? ExcludeFromBom { get; set; }

        [JsonProperty("exclude_from_board")]
        public string? ExcludeFromBoard { get; set; }

        [JsonProperty("exclude_from_sim")]
        public string? ExcludeFromSim { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("keywords")]
        public string? Keywords { get; set; }

        [JsonProperty("fields")]
        public Dictionary<string, FieldDetail>? Fields { get; set; }
    }
}
