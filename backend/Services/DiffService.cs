using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class DiffService
    {
        private readonly ApplicationDbContext _db;
        private readonly IBlobStore _blobStore;

        public DiffService(ApplicationDbContext db, IBlobStore blobStore)
        {
            _db = db;
            _blobStore = blobStore;
        }

        public async Task<object?> GetTextDiffAsync(string entity, int id, int fromRev, int toRev, CancellationToken cancellationToken = default)
        {
            (string? fromText, string fromLabel) = await GetTextRevisionAsync(entity, id, fromRev, cancellationToken);
            (string? toText, string toLabel) = await GetTextRevisionAsync(entity, id, toRev, cancellationToken);
            if (fromText == null && toText == null)
            {
                return null;
            }

            string diff = BuildUnifiedDiff(fromText ?? "", toText ?? "", fromLabel, toLabel);
            return new { format = "unified", diff };
        }

        public async Task<object?> GetBinaryDiffAsync(string entity, int id, int fromRev, int toRev, CancellationToken cancellationToken = default)
        {
            object? fromMeta = await GetBinaryRevisionMetaAsync(entity, id, fromRev, cancellationToken);
            object? toMeta = await GetBinaryRevisionMetaAsync(entity, id, toRev, cancellationToken);
            if (fromMeta == null && toMeta == null)
            {
                return null;
            }

            return new { format = "binary", from = fromMeta, to = toMeta };
        }

        public string BuildUnifiedDiff(string oldText, string newText, string oldLabel, string newLabel)
        {
            Differ differ = new Differ();
            InlineDiffBuilder builder = new InlineDiffBuilder(differ);
            DiffPaneModel model = builder.BuildDiffModel(oldText, newText);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"--- {oldLabel}");
            sb.AppendLine($"+++ {newLabel}");

            foreach (DiffPiece line in model.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        sb.Append('+').AppendLine(line.Text);
                        break;
                    case ChangeType.Deleted:
                        sb.Append('-').AppendLine(line.Text);
                        break;
                    case ChangeType.Modified:
                        sb.Append('~').AppendLine(line.Text);
                        break;
                    default:
                        sb.Append(' ').AppendLine(line.Text);
                        break;
                }
            }

            return sb.ToString();
        }

        private async Task<(string? Text, string Label)> GetTextRevisionAsync(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            string? hash = null;
            string name = $"{entity}/{id}@r{revisionNo}";

            if (entity == "symbol")
            {
                SymbolRevision? rev = await _db.SymbolRevisions.FirstOrDefaultAsync(
                    r => r.SymbolId == id && r.RevisionNo == revisionNo, cancellationToken);
                if (rev == null) return (null, name);
                hash = rev.BlobHash?.Trim();
                name = rev.Name;
            }
            else if (entity == "footprint")
            {
                FootprintRevision? rev = await _db.FootprintRevisions.FirstOrDefaultAsync(
                    r => r.FootprintId == id && r.RevisionNo == revisionNo, cancellationToken);
                if (rev == null) return (null, name);
                hash = rev.BlobHash?.Trim();
                name = rev.Name;
            }

            if (string.IsNullOrEmpty(hash))
            {
                return ("", $"{name} (r{revisionNo})");
            }

            Blob? blob = await _blobStore.GetAsync(hash, cancellationToken);
            string text = blob?.Data == null ? "" : Encoding.UTF8.GetString(blob.Data);
            return (text, $"{name} (r{revisionNo})");
        }

        private async Task<object?> GetBinaryRevisionMetaAsync(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            return entity switch
            {
                "model3d" => await _db.Model3DRevisions
                    .Where(r => r.Model3dId == id && r.RevisionNo == revisionNo)
                    .Select(r => new { hash = r.BlobHash, size = r.Size, filename = r.Filename, directory = r.Directory })
                    .FirstOrDefaultAsync(cancellationToken),
                "datasheet" => await _db.DatasheetRevisions
                    .Where(r => r.DatasheetId == id && r.RevisionNo == revisionNo)
                    .Select(r => new { hash = r.BlobHash, size = r.Size, filename = r.Filename, directory = r.Directory })
                    .FirstOrDefaultAsync(cancellationToken),
                "template" => await _db.TemplateRevisions
                    .Where(r => r.TemplateId == id && r.RevisionNo == revisionNo)
                    .Select(r => new { hash = r.BlobHash, size = r.Size, filename = r.Filename, directory = r.Directory })
                    .FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }
    }
}
