namespace KiComLibBackend.Models
{
    public class Model3DDTO
    {
        public int? Id { get; set; }
        public Guid? Uuid { get; set; }
        public required string Filename { get; set; }
        public string? Directory { get; set; }
        public string? Comment { get; set; }
        public long? FileSize { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public int? RevisionNo { get; set; }
        public int? HeadRevisionId { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
