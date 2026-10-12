using KiComLibBackend.Models;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class ProductImageResult
    {
        public int StatusCode { get; init; }

        public string? Error { get; init; }

        public string? Hash { get; init; }

        public string? MediaType { get; init; }

        public static ProductImageResult BadRequest(string error) => new() { StatusCode = 400, Error = error };

        public static ProductImageResult Stored(string hash, string mediaType) =>
            new() { StatusCode = 201, Hash = hash, MediaType = mediaType };
    }

    /// <summary>Stores a product photo in the blob table. Photos are at most 5 MB.</summary>
    public sealed class ProductImageStore
    {
        public const string HttpClientName = "product-image";
        public const int MaxBytes = 5 * 1024 * 1024;

        private readonly IHttpClientFactory _http;
        private readonly IBlobStore _blobs;
        private readonly ILogger<ProductImageStore> _logger;

        public ProductImageStore(IHttpClientFactory http, IBlobStore blobs, ILogger<ProductImageStore> logger)
        {
            _http = http;
            _blobs = blobs;
            _logger = logger;
        }

        public async Task<ProductImageResult> StoreAsync(byte[]? data, CancellationToken cancellationToken)
        {
            string? error = Validate(data, out string? mediaType);
            if (error != null || data == null || mediaType == null)
            {
                return ProductImageResult.BadRequest(error ?? "The file is not a supported image");
            }

            string hash = DbBlobStore.ComputeSha256Hex(data);
            await _blobs.PutAsync(hash, data, mediaType, cancellationToken);
            return ProductImageResult.Stored(hash, mediaType);
        }

        public async Task<ProductImageResult> ImportAsync(string? url, CancellationToken cancellationToken)
        {
            string? urlError = DistributorDatasheetImporter.ValidateUrl(url);
            if (urlError != null)
            {
                return ProductImageResult.BadRequest(urlError);
            }

            string target = url!.Trim();
            byte[]? data = null;
            string? downloadError = null;
            try
            {
                data = await DownloadAsync(target, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OutboundUrlException exception)
            {
                return ProductImageResult.BadRequest(exception.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Product image download failed for {Url}: {Detail}",
                    DistributorLog.Sanitize(target),
                    DistributorLog.Sanitize(exception.Message));
                downloadError = exception is TaskCanceledException
                    ? "The product image download timed out"
                    : "The product image could not be downloaded";
            }

            if (data == null)
            {
                return ProductImageResult.BadRequest(downloadError ?? "The product image is larger than 5 MB");
            }

            if (LooksLikeHtml(data))
            {
                return ProductImageResult.BadRequest("The image host returned a web page instead of an image file");
            }

            return await StoreAsync(data, cancellationToken);
        }

        public static string? Validate(byte[]? data, out string? mediaType)
        {
            mediaType = null;
            if (data == null || data.Length == 0)
            {
                return "An image file is required";
            }

            if (data.Length > MaxBytes)
            {
                return "The product image is larger than 5 MB";
            }

            mediaType = DetectMediaType(data);
            if (mediaType == null)
            {
                return "The product image must be a JPEG, PNG, GIF, or WebP file";
            }

            return null;
        }

        public static string? DetectMediaType(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (data.Length >= 8
                && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
                && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            {
                return "image/png";
            }

            if (data.Length >= 6
                && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8'
                && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
            {
                return "image/gif";
            }

            if (data.Length >= 12
                && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
                && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            {
                return "image/webp";
            }

            return null;
        }

        public static string? NormalizeHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return null;
            }

            string text = hash.Trim().ToLowerInvariant();
            if (text.Length != 64)
            {
                return null;
            }

            foreach (char character in text)
            {
                if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                {
                    return null;
                }
            }

            return text;
        }

        private static bool LooksLikeHtml(byte[] data)
        {
            int index = 0;
            while (index < data.Length && index < 64 && data[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                index++;
            }

            int length = Math.Min(data.Length - index, 15);
            if (length < 5)
            {
                return false;
            }

            string start = System.Text.Encoding.ASCII.GetString(data, index, length);
            return start.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            HttpClient client = _http.CreateClient(HttpClientName);
            using HttpResponseMessage response = await OutboundUrl.GetAsync(client, url, request =>
            {
                request.Headers.TryAddWithoutValidation("User-Agent", DistributorDatasheetImporter.BrowserUserAgent);
                request.Headers.TryAddWithoutValidation(
                    "Accept",
                    "image/jpeg,image/png,image/gif,image/webp,image/*;q=0.8");
            }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("The image host returned " + (int)response.StatusCode);
            }

            long? advertised = response.Content.Headers.ContentLength;
            if (advertised is > MaxBytes)
            {
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (buffer.Length + read > MaxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }
}
