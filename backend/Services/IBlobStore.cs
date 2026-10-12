using KiComLibBackend.Models;

namespace KiComLibBackend.Services
{
    public interface IBlobStore
    {
        Task<bool> ExistsAsync(string hash, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> FindMissingAsync(IEnumerable<string> hashes, CancellationToken cancellationToken = default);

        Task<Blob?> GetAsync(string hash, CancellationToken cancellationToken = default);

        /// <summary>Returns true if newly created, false if already existed.</summary>
        Task<bool> PutAsync(string expectedHash, byte[] data, string mediaType, CancellationToken cancellationToken = default);
    }
}
