using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using KiComLibBackend.Models;

namespace KiComLibBackend.Services.Distributors
{
    public sealed class DatasheetImportOutcome
    {
        public int StatusCode { get; init; }

        public string? Error { get; init; }

        public int Id { get; init; }

        public Guid Uuid { get; init; }

        public string Filename { get; init; } = "";

        public string Directory { get; init; } = "";

        public static DatasheetImportOutcome BadRequest(string error) => new() { StatusCode = 400, Error = error };
    }

    public sealed class DistributorDatasheetImporter
    {
        public const string HttpClientName = "distributor-datasheet";
        public const string DirectoryName = "distributor";
        public const int MaxBytes = 30 * 1024 * 1024;

        private readonly IHttpClientFactory _http;
        private readonly IBlobStore _blobs;
        private readonly CommitService _commits;
        private readonly ILogger<DistributorDatasheetImporter> _logger;

        public DistributorDatasheetImporter(
            IHttpClientFactory http,
            IBlobStore blobs,
            CommitService commits,
            ILogger<DistributorDatasheetImporter> logger)
        {
            _http = http;
            _blobs = blobs;
            _commits = commits;
            _logger = logger;
        }

        public async Task<DatasheetImportOutcome> ImportAsync(
            string? url,
            string? filename,
            string? author,
            CancellationToken cancellationToken)
        {
            string? urlError = ValidateUrl(url);
            if (urlError != null)
            {
                return DatasheetImportOutcome.BadRequest(urlError);
            }

            string source = url!.Trim();
            if (string.IsNullOrWhiteSpace(author))
            {
                return DatasheetImportOutcome.BadRequest("author is required");
            }

            string safeName = SanitizeFilename(filename, source);
            byte[]? data;
            string? media;
            try
            {
                (data, media) = await DownloadAsync(source, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DatasheetDownloadException exception)
            {
                return DatasheetImportOutcome.BadRequest(exception.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Datasheet download failed for {Url}: {Detail}",
                    DistributorLog.Sanitize(source),
                    DistributorLog.Sanitize(exception.Message));
                string message = exception is TaskCanceledException
                    ? "The datasheet download timed out"
                    : "The datasheet could not be downloaded";
                return DatasheetImportOutcome.BadRequest(message);
            }

            if (data == null)
            {
                return DatasheetImportOutcome.BadRequest("Datasheet is larger than 30 MB");
            }

            string? pdfError = ValidatePdf(data, media);
            if (pdfError != null)
            {
                _logger.LogWarning(
                    "Datasheet download from {Url} was not a PDF (content type {Media}, {Bytes} bytes)",
                    DistributorLog.Sanitize(source),
                    media ?? "",
                    data.Length);
                return DatasheetImportOutcome.BadRequest(pdfError);
            }

            string hash = DbBlobStore.ComputeSha256Hex(data);
            await _blobs.PutAsync(hash, data, "application/pdf", cancellationToken);
            string comment = "Imported from " + source;
            if (comment.Length > 1000)
            {
                comment = comment[..1000];
            }

            Guid uuid = Guid.NewGuid();
            CommitOutcome outcome = await _commits.ApplyAsync(
                new CommitRequest
                {
                    Message = "Import datasheet from distributor",
                    Client = "distributor",
                    Changes = new List<CommitChange>
                    {
                        new()
                        {
                            Entity = "datasheet",
                            Op = "create",
                            Uuid = uuid,
                            BlobHash = hash,
                            Filename = safeName,
                            Directory = DirectoryName,
                            Comment = comment
                        }
                    }
                },
                author.Trim(),
                cancellationToken);

            if (outcome.StatusCode == 201 && outcome.Response?.Results.Count > 0)
            {
                CommitResultItem item = outcome.Response.Results[0];
                return new DatasheetImportOutcome
                {
                    StatusCode = 201,
                    Id = item.Id,
                    Uuid = item.Uuid,
                    Filename = safeName,
                    Directory = DirectoryName
                };
            }

            if (outcome.Conflicts?.Any(conflict => conflict.Reason == "exists") == true)
            {
                return DatasheetImportOutcome.BadRequest("A datasheet with this filename already exists");
            }

            if (outcome.MissingBlobs?.Count > 0)
            {
                return DatasheetImportOutcome.BadRequest("The datasheet file could not be stored");
            }

            return DatasheetImportOutcome.BadRequest(string.IsNullOrWhiteSpace(outcome.Error)
                ? "Could not import datasheet"
                : outcome.Error);
        }

        public static string? ValidateUrl(string? url)
        {
            return OutboundUrl.SyntaxError(url);
        }

        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

        private static readonly Regex RedirectAssignment = new(
            @"(?i)(?:reqUrl|window\.location(?:\.href)?)\s*=\s*['""](https?://[^'""\s]+)['""]",
            RegexOptions.Compiled);

        private static readonly Regex PdfReference = new(
            @"(?i)(?:https?://[^\s'""<>]+\.pdf(?:\?[^\s'""<>]*)?|(?:href|src)\s*=\s*['""]([^'""]+\.pdf(?:\?[^'""]*)?)['""])",
            RegexOptions.Compiled);

        public static string? ValidatePdf(byte[] data, string? contentType)
        {
            if (HasPdfMagic(data))
            {
                return null;
            }

            // Content type is only a hint. Distributor CDNs label real PDFs as
            // octet-stream, x-download, or even text/html. The header bytes decide.
            _ = contentType;
            return "The URL did not return a PDF";
        }

        public static bool HasPdfMagic(byte[] data)
        {
            int index = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                index = 3;
            }

            while (index < data.Length && index < 1024 && data[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0)
            {
                index++;
            }

            return index + 4 <= data.Length
                && data[index] == (byte)'%'
                && data[index + 1] == (byte)'P'
                && data[index + 2] == (byte)'D'
                && data[index + 3] == (byte)'F';
        }

        /// <summary>
        /// Distributor links such as TI's suppproductinfo page put the real document in gotoUrl
        /// and return an HTML shell. The shell is not a PDF.
        /// </summary>
        public static string? UnwrapDocumentUrl(string url)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
            {
                return null;
            }

            string query = uri.Query.Length > 0 ? uri.Query[1..] : "";
            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string name = Uri.UnescapeDataString(pair[..eq]);
                if (!name.Equals("gotoUrl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = DecodeQueryValue(pair[(eq + 1)..]);
                string? resolved = ResolveHttpUrl(uri, value);
                if (resolved != null && !SameUrl(resolved, uri.AbsoluteUri))
                {
                    return resolved;
                }
            }

            return null;
        }

        public static string? ExtractRedirectUrl(string pageUrl, byte[] body)
        {
            if (body.Length == 0 || HasPdfMagic(body) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri? page))
            {
                return null;
            }

            int count = Math.Min(body.Length, 65536);
            string text = Encoding.UTF8.GetString(body, 0, count);
            foreach (Match match in RedirectAssignment.Matches(text))
            {
                string? resolved = ResolveHttpUrl(page, WebUtility.HtmlDecode(match.Groups[1].Value));
                if (resolved != null && !SameUrl(resolved, page.AbsoluteUri))
                {
                    return resolved;
                }
            }

            foreach (Match match in PdfReference.Matches(text))
            {
                string raw = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
                string? resolved = ResolveHttpUrl(page, WebUtility.HtmlDecode(raw));
                if (resolved != null && !SameUrl(resolved, page.AbsoluteUri))
                {
                    return resolved;
                }
            }

            return null;
        }

        public static string SanitizeFilename(string? requested, string url)
        {
            string name = (requested ?? "").Trim();
            if (name.Length == 0 && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                name = Uri.UnescapeDataString(uri.AbsolutePath);
            }

            name = name.Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name[(slash + 1)..];
            }

            StringBuilder builder = new(name.Length);
            foreach (char c in name)
            {
                if (c < 32 || c is '<' or '>' or ':' or '"' or '|' or '?' or '*' || c == '/' || c == '\\')
                {
                    continue;
                }

                builder.Append(c);
            }

            name = builder.ToString().Trim().Trim('.');
            if (name.Length == 0)
            {
                name = "datasheet.pdf";
            }

            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                name += ".pdf";
            }

            if (name.Length > 180)
            {
                name = name[..176].TrimEnd('.') + ".pdf";
            }

            return name;
        }

        private async Task<(byte[]? Data, string? MediaType)> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            string target = UnwrapDocumentUrl(url) ?? url;
            (byte[]? data, string? media) = await FetchAsync(target, cancellationToken);
            if (data == null || ValidatePdf(data, media) == null)
            {
                return (data, media);
            }

            string? next = ExtractRedirectUrl(target, data);
            if (next != null && !SameUrl(next, target))
            {
                _logger.LogInformation(
                    "Datasheet page {Url} pointed at {Next}",
                    DistributorLog.Sanitize(target),
                    DistributorLog.Sanitize(next));
                (data, media) = await FetchAsync(next, cancellationToken);
                if (data == null || ValidatePdf(data, media) == null)
                {
                    return (data, media);
                }
            }

            return (data, media);
        }

        private async Task<(byte[]? Data, string? MediaType)> FetchAsync(string url, CancellationToken cancellationToken)
        {
            HttpClient client = _http.CreateClient(HttpClientName);
            HttpResponseMessage response;
            try
            {
                response = await OutboundUrl.GetAsync(client, url, request =>
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
                    request.Headers.TryAddWithoutValidation("Accept", "application/pdf, */*;q=0.1");
                }, cancellationToken);
            }
            catch (OutboundUrlException exception)
            {
                throw new DatasheetDownloadException(exception.Message);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Datasheet download failed with status {Status} for {Url}",
                        (int)response.StatusCode,
                        DistributorLog.Sanitize(url));
                    throw new DatasheetDownloadException("The datasheet could not be downloaded");
                }

                if (response.Content.Headers.ContentLength is long length && length > MaxBytes)
                {
                    return (null, null);
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using MemoryStream buffer = new();
                byte[] chunk = new byte[81920];
                int total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaxBytes)
                    {
                        return (null, null);
                    }

                    buffer.Write(chunk, 0, read);
                }

                return (buffer.ToArray(), response.Content.Headers.ContentType?.MediaType);
            }
        }

        private static string DecodeQueryValue(string raw)
        {
            string value = raw;
            for (int pass = 0; pass < 2; pass++)
            {
                string decoded = Uri.UnescapeDataString(value);
                if (decoded == value)
                {
                    break;
                }

                value = decoded;
            }

            return value.Trim();
        }

        private static string? ResolveHttpUrl(Uri page, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!Uri.TryCreate(page, value.Trim(), out Uri? target))
            {
                return null;
            }

            return ValidateUrl(target.AbsoluteUri) == null ? target.AbsoluteUri : null;
        }

        private static bool SameUrl(string left, string right)
        {
            return string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private sealed class DatasheetDownloadException : Exception
        {
            public DatasheetDownloadException(string message)
                : base(message)
            {
            }
        }
    }
}
