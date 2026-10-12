using System.Security.Cryptography;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace KiComLibBackend.Services
{
    public class DbBlobStore : IBlobStore
    {
        private readonly ApplicationDbContext _db;

        public DbBlobStore(ApplicationDbContext db)
        {
            _db = db;
        }

        public static string ComputeSha256Hex(byte[] data)
        {
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        public static string NormalizeHash(string hash) => hash.Trim().ToLowerInvariant();

        public async Task<bool> ExistsAsync(string hash, CancellationToken cancellationToken = default)
        {
            string normalized = NormalizeHash(hash);
            return await _db.Blobs.AnyAsync(b => b.Hash == normalized, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> FindMissingAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default)
        {
            List<string> normalized = hashes.Select(NormalizeHash).Distinct(StringComparer.Ordinal).ToList();
            if (normalized.Count == 0)
            {
                return Array.Empty<string>();
            }

            HashSet<string> existing = (await _db.Blobs
                    .Where(b => normalized.Contains(b.Hash))
                    .Select(b => b.Hash)
                    .ToListAsync(cancellationToken))
                .Select(NormalizeHash)
                .ToHashSet(StringComparer.Ordinal);

            return normalized.Where(h => !existing.Contains(h)).ToList();
        }

        public async Task<Blob?> GetAsync(string hash, CancellationToken cancellationToken = default)
        {
            string normalized = NormalizeHash(hash);
            Blob? blob = await _db.Blobs.FirstOrDefaultAsync(b => b.Hash == normalized, cancellationToken);
            if (blob != null)
            {
                blob.Hash = NormalizeHash(blob.Hash);
            }

            return blob;
        }

        public async Task<bool> PutAsync(string expectedHash, byte[] data, string mediaType, CancellationToken cancellationToken = default)
        {
            string normalized = NormalizeHash(expectedHash);
            string actual = ComputeSha256Hex(data);
            if (!string.Equals(actual, normalized, StringComparison.Ordinal))
            {
                throw new BlobHashMismatchException(normalized, actual);
            }

            if (await ExistsAsync(normalized, cancellationToken))
            {
                return false;
            }

            _db.Blobs.Add(new Blob
            {
                Hash = normalized,
                Size = data.LongLength,
                MediaType = mediaType,
                Data = data,
                Storage = 0
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // Concurrent insert of same hash
                _db.ChangeTracker.Clear();
                if (await ExistsAsync(normalized, cancellationToken))
                {
                    return false;
                }

                throw;
            }
        }
    }

    public class BlobHashMismatchException : Exception
    {
        public string ExpectedHash { get; }
        public string ActualHash { get; }

        public BlobHashMismatchException(string expectedHash, string actualHash)
            : base($"Blob hash mismatch: expected {expectedHash}, got {actualHash}")
        {
            ExpectedHash = expectedHash;
            ActualHash = actualHash;
        }
    }
}
