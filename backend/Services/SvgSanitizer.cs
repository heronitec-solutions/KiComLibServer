using System.Xml;
using System.Xml.Linq;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Drops active content from an SVG document. Drawing elements stay.
    /// A document that is not XML comes back empty.
    /// </summary>
    public static class SvgSanitizer
    {
        private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "foreignObject", "iframe", "embed", "object", "image", "set",
            "animate", "animateTransform", "animateMotion", "handler", "audio", "video",
            "canvas", "link", "meta", "style"
        };

        public static string Sanitize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "";
            }

            try
            {
                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using StringReader text = new(StripDeclaration(raw));
                using XmlReader reader = XmlReader.Create(text, settings);
                XDocument document = XDocument.Load(reader, LoadOptions.None);
                if (document.Root == null || !LocalName(document.Root).Equals("svg", StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }

                Clean(document.Root);
                return document.Root.ToString(SaveOptions.DisableFormatting);
            }
            catch (XmlException)
            {
                return "";
            }
        }

        public static byte[]? SanitizeBytes(byte[] data)
        {
            string clean = Sanitize(System.Text.Encoding.UTF8.GetString(data));
            return clean.Length == 0 ? null : System.Text.Encoding.UTF8.GetBytes(clean);
        }

        private static string StripDeclaration(string raw)
        {
            string text = raw.Trim();
            if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                int end = text.IndexOf("?>", StringComparison.Ordinal);
                if (end >= 0)
                {
                    text = text[(end + 2)..].TrimStart();
                }
            }

            if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                int end = text.IndexOf('>');
                if (end >= 0)
                {
                    text = text[(end + 1)..].TrimStart();
                }
            }

            return text;
        }

        private static void Clean(XElement element)
        {
            List<XElement> children = element.Elements().ToList();
            foreach (XElement child in children)
            {
                if (Dropped.Contains(LocalName(child)))
                {
                    child.Remove();
                    continue;
                }

                Clean(child);
            }

            List<XAttribute> attributes = element.Attributes().ToList();
            foreach (XAttribute attribute in attributes)
            {
                if (!Keep(attribute))
                {
                    attribute.Remove();
                }
            }
        }

        private static bool Keep(XAttribute attribute)
        {
            string name = attribute.Name.LocalName;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (name.Equals("href", StringComparison.OrdinalIgnoreCase)
                || name.Equals("src", StringComparison.OrdinalIgnoreCase))
            {
                return SafeReference(attribute.Value);
            }

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                return SafeStyle(attribute.Value);
            }

            return true;
        }

        private static bool SafeReference(string value)
        {
            string trimmed = value.Trim();
            return trimmed.StartsWith('#') && !trimmed.Contains(':') && !trimmed.Contains('\\');
        }

        private static bool SafeStyle(string value)
        {
            string lower = value.ToLowerInvariant();
            return !lower.Contains("url(")
                && !lower.Contains("expression")
                && !lower.Contains("javascript")
                && !lower.Contains("@import")
                && !lower.Contains("behavior");
        }

        private static string LocalName(XElement element)
        {
            return element.Name.LocalName;
        }
    }
}
