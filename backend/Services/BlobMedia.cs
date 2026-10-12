using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Media types a client may store, and how stored bytes are sent back.
    /// The stored label is not trusted for anything the browser would execute.
    /// </summary>
    public static class BlobMedia
    {
        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            "text/x-kicad-sexpr",
            "text/csv",
            "application/json",
            "application/pdf",
            "application/zip",
            "application/octet-stream",
            "application/x-tar",
            "image/svg+xml",
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp",
            "model/step",
            "model/vrml"
        };

        public static string? Normalize(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return null;
            }

            string value = mediaType.Trim();
            int separator = value.IndexOf(';');
            if (separator >= 0)
            {
                value = value[..separator].Trim();
            }

            return value.ToLowerInvariant();
        }

        public static bool TryAccept(string? mediaType, out string normalized, out string? error)
        {
            normalized = Normalize(mediaType) ?? "";
            if (normalized.Length == 0)
            {
                error = "X-Media-Type header is required";
                return false;
            }

            if (!Allowed.Contains(normalized))
            {
                error = "This media type cannot be stored";
                return false;
            }

            error = null;
            return true;
        }

        public static IActionResult File(ControllerBase controller, byte[] data, string? storedMediaType, string? fileName, bool download)
        {
            string mediaType = "application/octet-stream";
            bool inline = false;
            if (DistributorDatasheetImporterLikePdf(data))
            {
                mediaType = "application/pdf";
                inline = !download;
            }
            else if (Distributors.ProductImageStore.DetectMediaType(data) is string image)
            {
                mediaType = image;
                inline = !download;
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                ContentDispositionHeaderValue disposition = new(inline ? "inline" : "attachment");
                disposition.SetHttpFileName(fileName);
                controller.Response.Headers.ContentDisposition = disposition.ToString();
            }
            else if (!inline)
            {
                controller.Response.Headers.ContentDisposition = "attachment";
            }

            return controller.File(data, mediaType);
        }

        private static bool DistributorDatasheetImporterLikePdf(byte[] data)
        {
            return Distributors.DistributorDatasheetImporter.HasPdfMagic(data);
        }
    }
}
