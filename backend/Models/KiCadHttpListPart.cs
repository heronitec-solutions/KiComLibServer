namespace KiComLibBackend.Models
{
    public class KiCadHttpListPart
    {
        public required string Id { get; set; }

        public required string Name { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public int? LibraryId { get; set; }

        public string? Description { get; set; }
    }
}
