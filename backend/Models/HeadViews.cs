using System.ComponentModel.DataAnnotations.Schema;

namespace KiComLibBackend.Models
{
    /// <summary>Keyless mapping for symbol_head_v.</summary>
    public class SymbolHeadView
    {
        public int Id { get; set; }
        public Guid Uuid { get; set; }
        public int LibraryFileId { get; set; }
        public string Name { get; set; } = "";
        public int? HeadRevisionId { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? RevisionNo { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public string? PreviewBlobHash { get; set; }
        public string? Reference { get; set; }
        public bool InBom { get; set; }
        public bool OnBoard { get; set; }
        public bool PinNumbersHidden { get; set; }
        public bool PinNamesHidden { get; set; }
        public float? PinNamesOffset { get; set; }
        public string? Description { get; set; }
        public int? CommitId { get; set; }
        public string? LibraryFileName { get; set; }
        public string? LibraryFileFilename { get; set; }
        public string? LibraryFileVersion { get; set; }
    }

    public class FootprintHeadView
    {
        public int Id { get; set; }
        public Guid Uuid { get; set; }
        public int LibraryFileId { get; set; }
        public string Name { get; set; } = "";
        public int? HeadRevisionId { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? RevisionNo { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public string? PreviewBlobHash { get; set; }
        public int Type { get; set; }
        public bool BoardOnly { get; set; }
        public bool ExcludeFromPosFiles { get; set; }
        public bool ExcludeFromBom { get; set; }
        public float Height { get; set; }
        public string Layer { get; set; } = "";
        public string? Description { get; set; }
        public string? Tags { get; set; }
        public string? Property { get; set; }
        public int? CommitId { get; set; }
        public int? Model3dId { get; set; }
        public string? LibraryFileName { get; set; }
        public string? LibraryFileFilename { get; set; }
        public string? LibraryFileVersion { get; set; }
    }

    public class Model3DHeadView
    {
        public int Id { get; set; }
        public Guid Uuid { get; set; }
        public string Directory { get; set; } = "";
        public string Filename { get; set; } = "";
        public int? HeadRevisionId { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? RevisionNo { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public string? Comment { get; set; }
        public long? FileSize { get; set; }
        public int? CommitId { get; set; }
    }

    public class DatasheetHeadView
    {
        public int Id { get; set; }
        public Guid Uuid { get; set; }
        public string Directory { get; set; } = "";
        public string Filename { get; set; } = "";
        public int? HeadRevisionId { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? RevisionNo { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public string? Comment { get; set; }
        public long? FileSize { get; set; }
        public int? CommitId { get; set; }
    }

    public class TemplateHeadView
    {
        public int Id { get; set; }
        public Guid Uuid { get; set; }
        public string Directory { get; set; } = "";
        public string Filename { get; set; } = "";
        public int? HeadRevisionId { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? RevisionNo { get; set; }
        public string? Hash { get; set; }
        public string? FileHash { get; set; }
        public string? Comment { get; set; }
        public long? FileSize { get; set; }
        public int? CommitId { get; set; }
    }
}
