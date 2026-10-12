using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Bytes for project outputs live in project_blob. They are not served from /api/blob.
    /// </summary>
    public class ProjectBlobStore
    {
        private readonly ApplicationDbContext _db;

        public ProjectBlobStore(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> PutAsync(byte[] data, string mediaType, CancellationToken cancellationToken)
        {
            string hash = ProjectExport.Sha256Hex(data);
            bool exists = await _db.ProjectBlobs.AnyAsync(b => b.Hash == hash, cancellationToken);
            if (!exists)
            {
                _db.ProjectBlobs.Add(new ProjectBlob
                {
                    Hash = hash,
                    Size = data.LongLength,
                    MediaType = string.IsNullOrWhiteSpace(mediaType) ? "application/octet-stream" : mediaType,
                    Data = data,
                    Storage = 0,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return hash;
        }

        public Task<ProjectBlob?> GetAsync(string hash, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return Task.FromResult<ProjectBlob?>(null);
            }

            return _db.ProjectBlobs.FirstOrDefaultAsync(b => b.Hash == hash, cancellationToken);
        }
    }
}
