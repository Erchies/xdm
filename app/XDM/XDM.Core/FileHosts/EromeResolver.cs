using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Translations;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Lists the videos and images of an erome.com album. Port of the collecting half of
    /// M4p4/EromeDownloader; the CDN refuses requests without the album page as Referer.
    /// </summary>
    public class EromeResolver : IFileHostResolver
    {
        public const string HostName = "www.erome.com";
        public const string UserAgent = "Mozilla/5.0";

        private static readonly Regex HostRegex = new(@"(^|\.)erome\.com$", RegexOptions.IgnoreCase);
        private static readonly Regex AlbumPathRegex = new(@"^/a/([A-Za-z0-9]+)/?$", RegexOptions.IgnoreCase);
        private static readonly string[] ImageClasses = { "img" };

        private readonly IFileHostHttp http;
        private readonly FileHostOptions options;

        public EromeResolver(IFileHostHttp http, FileHostOptions options)
        {
            this.http = http;
            this.options = options;
        }

        public string Name => "Erome";

        public bool CanHandle(string url) => IsEromeLink(url);

        public static bool IsEromeLink(string url) => TryParseAlbum(url, out _, out _);

        public IList<ResolvedFile> Resolve(FileHostLink link, FileHostContext context)
        {
            if (!TryParseAlbum(link.Url, out var albumUrl, out var albumId))
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_INVALID_URL"), link.Url));
            }

            var response = FileHostRequest.Send(http, context, options, "GET", albumUrl!, PageHeaders());
            if (!response.IsSuccess)
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_PAGE_FAILED"), albumUrl, response.StatusCode));
            }

            // The CDN checks the Referer, so send the address the page actually came from.
            var pageUrl = response.ResponseUri?.AbsoluteUri ?? albumUrl!;
            var title = GetAlbumTitle(response.Body) ?? albumId!;
            var mediaUrls = GetMediaUrls(response.Body, pageUrl);

            context.Info(string.Format(TextResource.GetText("FH_ALBUM"), title, mediaUrls.Count));
            if (mediaUrls.Count == 0)
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_ALBUM_EMPTY"), link.Url));
            }

            var folder = options.CreateSubfolders
                ? FileHostNames.Sanitize(title, FileHostNames.Sanitize(albumId, "erome"))
                : string.Empty;
            var names = new FileHostNameRegistry();
            var files = new List<ResolvedFile>();

            foreach (var mediaUrl in mediaUrls)
            {
                var name = FileHostNames.Sanitize(LastPathSegment(mediaUrl), "erome-file");
                if (options.ShouldSkip(name))
                {
                    context.Info(string.Format(TextResource.GetText("FH_FILTERED"), name));
                    continue;
                }

                files.Add(new ResolvedFile
                {
                    Host = Name,
                    SourceUrl = link.Url,
                    Name = names.Reserve(folder, name),
                    RelativeFolder = folder,
                    Url = mediaUrl,
                    Headers = new Dictionary<string, List<string>>
                    {
                        ["User-Agent"] = new() { UserAgent },
                        ["Referer"] = new() { pageUrl }
                    }
                });
            }

            return files;
        }

        /// <summary>
        /// Album name from the Open Graph title.
        /// </summary>
        public static string? GetAlbumTitle(string html)
        {
            foreach (var meta in SimpleHtml.FindTags(html, "meta"))
            {
                if (!string.Equals(meta.Attribute("property"), "og:title", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var title = (meta.Attribute("content") ?? string.Empty).Trim();
                return title.Length == 0 ? null : title;
            }
            return null;
        }

        /// <summary>
        /// Every video (&lt;source src&gt;) and image (&lt;div class="img" data-src&gt;) of the album,
        /// in page order and without repeats.
        /// </summary>
        public static List<string> GetMediaUrls(string html, string? pageUrl = null)
        {
            var urls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string? value)
            {
                var absolute = ToAbsoluteUrl(value, pageUrl);
                if (absolute != null && seen.Add(absolute))
                {
                    urls.Add(absolute);
                }
            }

            foreach (var source in SimpleHtml.FindTags(html, "source"))
            {
                Add(source.Attribute("src"));
            }
            foreach (var image in SimpleHtml.FindTags(html, "div"))
            {
                if (image.HasClasses(ImageClasses))
                {
                    Add(image.Attribute("data-src"));
                }
            }

            return urls;
        }

        internal static bool TryParseAlbum(string url, out string? albumUrl, out string? albumId)
        {
            albumUrl = null;
            albumId = null;

            if (!Uri.TryCreate(FileHostLinkParser.EnsureScheme(url ?? string.Empty), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !HostRegex.IsMatch(uri.Host))
            {
                return false;
            }

            var match = AlbumPathRegex.Match(uri.AbsolutePath);
            if (!match.Success)
            {
                return false;
            }

            albumId = match.Groups[1].Value;
            // erome serves albums from www only; a bare erome.com link would just redirect.
            albumUrl = $"https://{HostName}/a/{albumId}";
            return true;
        }

        private static string? ToAbsoluteUrl(string? value, string? pageUrl)
        {
            var candidate = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
            if (candidate.Length == 0)
            {
                return null;
            }
            if (candidate.StartsWith("//"))
            {
                candidate = "https:" + candidate;
            }
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && pageUrl != null
                && Uri.TryCreate(new Uri(pageUrl), candidate, out var relative))
            {
                uri = relative;
            }
            return uri != null && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri : null;
        }

        private static string LastPathSegment(string url)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            var segment = path.TrimEnd('/');
            return Uri.UnescapeDataString(segment.Substring(segment.LastIndexOf('/') + 1));
        }

        private static Dictionary<string, string> PageHeaders() => new()
        {
            ["User-Agent"] = UserAgent,
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        };
    }
}
