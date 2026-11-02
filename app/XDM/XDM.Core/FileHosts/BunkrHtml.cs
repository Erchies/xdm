using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Extracts what the Bunkr downloader needs from album and file pages. The markup is
    /// predictable Tailwind output, so a handful of targeted patterns replace BeautifulSoup
    /// without adding an HTML parser dependency to every XDM front end.
    /// </summary>
    public static class BunkrHtml
    {
        private static readonly Regex ScriptRegex = new(@"<script\b[^>]*>(.*?)</script\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex JsVarsRegex = new(@"var\s+(\w+)\s*=\s*("".*?""|'.*?'|[^;]+);", RegexOptions.Singleline);
        private static readonly Regex MediaSlugRegex = new(@"const\s+slug\s*=\s*""([a-zA-Z0-9_-]+)""");
        private static readonly Regex UnicodeEscapeRegex = new(@"\\u([0-9a-fA-F]{4})");
        private static readonly Regex NumberRegex = new(@"\d+");
        private static readonly Regex RelativeDateRegex = new(@"\bago\b|\byesterday\b|\btoday\b", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"
        };

        // Same formats as date_utils.py: ISO 8601 first, then the strptime list. Values without
        // a time zone are taken as UTC.
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd",
            "d/M/yyyy", "MMMM d, yyyy", "MMM d, yyyy", "H:mm:ss d/M/yyyy"
        };

        private static readonly string[] ItemLinkClasses = { "after:absolute", "after:z-10", "after:inset-0" };
        private static readonly string[] ItemNameClasses = { "text-subs", "font-semibold", "text-base", "sm:text-lg", "truncate" };
        private static readonly string[] AlbumNameClasses = { "text-subs", "font-semibold", "flex", "text-base", "sm:text-lg" };
        private static readonly string[] StatusRowClasses = { "flex", "items-center", "gap-4", "py-4", "border-b", "border-soft" };

        /// <summary>
        /// Item page links of an album page, made absolute against <paramref name="hostPage"/>.
        /// </summary>
        public static List<string> GetItemPages(string html, string hostPage)
        {
            return GetItemLinks(html, hostPage).Distinct().ToList();
        }

        /// <summary>
        /// Upload date of each item of an album page, keyed by item page link. The "theDate"
        /// elements are paired with the item links by position, as in date_utils.py, so the
        /// links are taken with their repeats. Items whose date does not parse are left out.
        /// </summary>
        public static Dictionary<string, DateTime> GetItemDates(string html, string hostPage)
        {
            var dates = SimpleHtml.FindTags(html, null)
                .Where(tag => tag.HasClasses(new[] { "theDate" }))
                .Select(tag => ParseDate(SimpleHtml.Text(SimpleHtml.InnerHtml(html, tag))))
                .ToList();
            var links = GetItemLinks(html, hostPage);

            var result = new Dictionary<string, DateTime>();
            for (var i = 0; i < Math.Min(links.Count, dates.Count); i++)
            {
                if (dates[i] is DateTime date)
                {
                    result[links[i]] = date;
                }
                else
                {
                    result.Remove(links[i]);
                }
            }
            return result;
        }

        private static List<string> GetItemLinks(string html, string hostPage)
        {
            var links = new List<string>();
            foreach (var tag in SimpleHtml.FindTags(html, "a"))
            {
                var href = tag.Attribute("href");
                if (string.IsNullOrEmpty(href) || !tag.HasClasses(ItemLinkClasses))
                {
                    continue;
                }
                links.Add(Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http")
                    ? href! : hostPage.TrimEnd('/') + "/" + href!.TrimStart('/'));
            }
            return links;
        }

        /// <summary>
        /// Upload date shown on an item page: a &lt;time datetime&gt; tag, or the tooltip of a
        /// relative date such as "3 days ago". Upstream has not confirmed this markup against a
        /// live page either, so it returns null rather than guessing.
        /// </summary>
        public static DateTime? GetItemDate(string html)
        {
            if (SimpleHtml.FindTags(html, "time").FirstOrDefault()?.Attribute("datetime") is string datetime
                && ParseDate(datetime) is DateTime fromTime)
            {
                return fromTime;
            }

            foreach (var tag in SimpleHtml.FindTags(html, null))
            {
                // Void elements have no text; InnerHtml would run on into the rest of the page.
                if (!VoidElements.Contains(tag.Name)
                    && tag.Attribute("title") is string title
                    && RelativeDateRegex.IsMatch(SimpleHtml.Text(SimpleHtml.InnerHtml(html, tag)))
                    && ParseDate(title) is DateTime fromTitle)
                {
                    return fromTitle;
                }
            }
            return null;
        }

        /// <summary>
        /// Parses a date as the Python downloader does, returning it in UTC, or null.
        /// </summary>
        public static DateTime? ParseDate(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length > 0 && DateTimeOffset.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowInnerWhite, out var parsed))
            {
                return parsed.UtcDateTime;
            }
            return null;
        }

        /// <summary>
        /// Highest page number in the album pagination, or 1 when there is none.
        /// </summary>
        public static int GetPageCount(string html)
        {
            foreach (var tag in SimpleHtml.FindTags(html, "nav"))
            {
                if (!tag.HasClasses(new[] { "pagination" }))
                {
                    continue;
                }
                // Tags become blanks so adjacent links "<a>1</a><a>2</a>" do not read as 12.
                var text = SimpleHtml.Text(SimpleHtml.InnerHtml(html, tag), " ");
                var numbers = NumberRegex.Matches(text)
                    .Cast<Match>()
                    .Select(m => int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
                    .ToList();
                return numbers.Count == 0 ? 1 : Math.Max(1, numbers.Max());
            }
            return 1;
        }

        public static string? GetAlbumName(string html)
        {
            foreach (var div in SimpleHtml.FindTags(html, "div"))
            {
                if (!div.HasClasses(AlbumNameClasses))
                {
                    continue;
                }
                var h1 = SimpleHtml.FindTags(html, "h1", div.End).FirstOrDefault();
                if (h1 == null)
                {
                    return null;
                }
                var name = FixMojibake(SimpleHtml.Text(SimpleHtml.InnerHtml(html, h1)));
                return name.Length == 0 ? null : name;
            }
            return null;
        }

        public static string? GetItemFileName(string html)
        {
            foreach (var h1 in SimpleHtml.FindTags(html, "h1"))
            {
                if (!h1.HasClasses(ItemNameClasses))
                {
                    continue;
                }
                var name = FixMojibake(SimpleHtml.Text(ReplaceCloudflareEmails(SimpleHtml.InnerHtml(html, h1))));
                return name.Length == 0 ? null : name;
            }
            return null;
        }

        /// <summary>
        /// Variables declared in the inline script that defines jsCDN.
        /// </summary>
        public static Dictionary<string, string> GetPageVariables(string html)
        {
            var variables = new Dictionary<string, string>();
            foreach (Match script in ScriptRegex.Matches(html))
            {
                var body = script.Groups[1].Value;
                if (!body.Contains("var jsCDN"))
                {
                    continue;
                }
                foreach (Match variable in JsVarsRegex.Matches(body))
                {
                    variables[variable.Groups[1].Value] = UnescapeJsPath(variable.Groups[2].Value).Trim().Trim('"', '\'');
                }
                break;
            }
            return variables;
        }

        /// <summary>
        /// Undoes the escaping javascript applies to a path. Bunkr started emitting \uXXXX for
        /// non-ASCII characters, which used to reach the CDN literally and give a 404.
        /// </summary>
        public static string UnescapeJsPath(string value)
        {
            value = UnicodeEscapeRegex.Replace(value,
                m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
            return value.Replace(@"\/", "/").Replace(@"\\", @"\");
        }

        public static string? GetFileId(string html)
        {
            foreach (var tag in SimpleHtml.FindTags(html, "script"))
            {
                var id = tag.Attribute("data-file-id");
                if (!string.IsNullOrEmpty(id))
                {
                    return id;
                }
            }
            return null;
        }

        public static string? GetMediaSlug(string html)
        {
            var match = MediaSlugRegex.Match(html);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Server name to status text, as listed on the Bunkr status page.
        /// </summary>
        public static Dictionary<string, string> GetServerStatus(string html)
        {
            var status = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rows = SimpleHtml.FindTags(html, "div").Where(t => t.HasClasses(StatusRowClasses)).ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                var end = i + 1 < rows.Count ? rows[i + 1].Start : html.Length;
                var row = html.Substring(rows[i].End, end - rows[i].End);
                var p = SimpleHtml.FindTags(row, "p").FirstOrDefault();
                var span = SimpleHtml.FindTags(row, "span").FirstOrDefault();
                if (p != null && span != null)
                {
                    status[SimpleHtml.Text(SimpleHtml.InnerHtml(row, p))] = SimpleHtml.Text(SimpleHtml.InnerHtml(row, span));
                }
            }
            return status;
        }

        /// <summary>
        /// Cloudflare replaces anything that looks like an e-mail with an XOR-obfuscated tag.
        /// </summary>
        public static string ReplaceCloudflareEmails(string fragment)
        {
            foreach (var tag in SimpleHtml.FindTags(fragment, "a").Concat(SimpleHtml.FindTags(fragment, "span")).OrderByDescending(t => t.Start).ToList())
            {
                var encoded = tag.Attribute("data-cfemail");
                if (string.IsNullOrEmpty(encoded) || !tag.HasClasses(new[] { "__cf_email__" }))
                {
                    continue;
                }
                var close = fragment.IndexOf("</" + tag.Name, tag.End, StringComparison.OrdinalIgnoreCase);
                var closeEnd = close < 0 ? tag.End : fragment.IndexOf('>', close) + 1;
                string decoded;
                try
                {
                    decoded = DecodeCloudflareEmail(encoded!);
                }
                catch (FormatException)
                {
                    continue;
                }
                fragment = fragment.Substring(0, tag.Start) + WebUtility.HtmlEncode(decoded) + fragment.Substring(closeEnd);
            }
            return fragment;
        }

        public static string DecodeCloudflareEmail(string hex)
        {
            if (hex.Length < 2 || hex.Length % 2 != 0)
            {
                throw new FormatException("Invalid data-cfemail value");
            }
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            var key = bytes[0];
            var decoded = bytes.Skip(1).Select(b => (byte)(b ^ key)).ToArray();
            return Encoding.UTF8.GetString(decoded);
        }

        /// <summary>
        /// Repairs UTF-8 text that was decoded as Latin-1 ("CafÃ©" -> "Café"). Text that is
        /// not in that shape is returned unchanged.
        /// </summary>
        public static string FixMojibake(string text)
        {
            if (text.All(c => c < 0x80) || text.Any(c => c > 0xFF))
            {
                return text;
            }
            var bytes = text.Select(c => (byte)c).ToArray();
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return text;
            }
        }
    }
}
