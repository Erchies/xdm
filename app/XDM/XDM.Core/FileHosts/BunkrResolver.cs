using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Translations;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Resolves Bunkr album (/a/) and file (/f/, /i/, /v/) pages into signed CDN links.
    /// Port of the crawler in ZeroHackz/BunkrDownloader-Portable (from Lysagxra/BunkrDownloader).
    /// </summary>
    public class BunkrResolver : IFileHostResolver
    {
        public const string SignApi = "https://glb-apisign.cdn.cr/sign";
        public const string DownloadApi = "https://dl.bunkr.cr/api/_001_v2";
        public const string DownloadReferer = "https://dl.bunkrr.cr/";
        public const string FallbackDomain = "bunkr.cr";
        public const string StatusPage = "https://status.bunkr.ru/";
        public const string UserAgent = "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:136.0) Gecko/20100101 Firefox/136.0";

        private static readonly Regex HostRegex = new(@"(^|\.)bunkrr?\.[a-z]{2,}$", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> AlbumTypes = new() { "a" };
        private static readonly HashSet<string> FileTypes = new() { "f", "i", "v" };

        private readonly IFileHostHttp http;
        private readonly FileHostOptions options;
        private Dictionary<string, string>? serverStatus;

        public BunkrResolver(IFileHostHttp http, FileHostOptions options)
        {
            this.http = http;
            this.options = options;
        }

        public string Name => "Bunkr";

        public bool CanHandle(string url) => IsBunkrLink(url);

        public static bool IsBunkrLink(string url)
        {
            return TryParse(url, out var uri) && HostRegex.IsMatch(uri!.Host) && GetUrlType(uri) != null;
        }

        public IList<ResolvedFile> Resolve(FileHostLink link, FileHostContext context)
        {
            if (!TryParse(link.Url, out var uri) || GetUrlType(uri!) is not string type)
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_INVALID_URL"), link.Url));
            }

            var pageUrl = RemovePageParameter(uri!);
            return AlbumTypes.Contains(type)
                ? ResolveAlbum(pageUrl, link.Url, context)
                : ResolveSingle(pageUrl, link.Url, context);
        }

        private IList<ResolvedFile> ResolveSingle(string pageUrl, string sourceUrl, FileHostContext context)
        {
            var page = FetchPage(pageUrl, context);
            var file = ResolveItem(page.Url, page.Html, string.Empty, sourceUrl, null, context);
            return file == null ? new List<ResolvedFile>() : new List<ResolvedFile> { file };
        }

        private IList<ResolvedFile> ResolveAlbum(string albumUrl, string sourceUrl, FileHostContext context)
        {
            var firstPage = FetchPage(albumUrl, context);
            var albumId = new Uri(albumUrl).Segments.Last().Trim('/');
            var albumName = BunkrHtml.GetAlbumName(firstPage.Html) ?? albumId;
            var hostPage = new Uri(firstPage.Url).GetLeftPart(UriPartial.Authority);

            var itemPages = BunkrHtml.GetItemPages(firstPage.Html, hostPage);
            var itemDates = BunkrHtml.GetItemDates(firstPage.Html, hostPage);
            var pageCount = BunkrHtml.GetPageCount(firstPage.Html);
            var baseUrl = RemovePageParameter(new Uri(firstPage.Url));
            for (var n = 2; n <= pageCount; n++)
            {
                var page = FetchPage(baseUrl + (baseUrl.Contains("?") ? "&" : "?") + "page=" + n, context);
                itemPages.AddRange(BunkrHtml.GetItemPages(page.Html, hostPage).Where(p => !itemPages.Contains(p)));
                foreach (var date in BunkrHtml.GetItemDates(page.Html, hostPage))
                {
                    itemDates[date.Key] = date.Value;
                }
            }

            context.Info(string.Format(TextResource.GetText("FH_BK_ALBUM"), albumName, itemPages.Count));
            if (itemPages.Count == 0)
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_BK_NO_ITEMS"), sourceUrl));
            }

            var folder = options.CreateSubfolders ? FileHostNames.Sanitize(albumName, FileHostNames.Sanitize(albumId, "bunkr")) : string.Empty;
            var results = new ResolvedFile?[itemPages.Count];
            var next = -1;
            var workers = Enumerable.Range(0, Math.Max(1, Math.Min(options.MaxParallelRequests, itemPages.Count)))
                .Select(_ => new Thread(() =>
                {
                    int index;
                    while ((index = Interlocked.Increment(ref next)) < itemPages.Count && !context.IsCancelled)
                    {
                        var itemUrl = itemPages[index];
                        DateTime? albumDate = itemDates.TryGetValue(itemUrl, out var date) ? date : null;
                        results[index] = TryResolveItemPage(itemUrl, folder, sourceUrl, albumDate, context);
                    }
                })
                { IsBackground = true, Name = "bunkr-resolver" })
                .ToList();

            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
            context.ThrowIfCancelled();

            // Names are made unique afterwards so the result does not depend on thread timing.
            var names = new FileHostNameRegistry();
            var files = new List<ResolvedFile>();
            foreach (var file in results)
            {
                if (file != null)
                {
                    file.Name = names.Reserve(file.RelativeFolder, file.Name);
                    files.Add(file);
                }
            }
            return files;
        }

        private ResolvedFile? TryResolveItemPage(string itemUrl, string folder, string sourceUrl, DateTime? albumDate, FileHostContext context)
        {
            try
            {
                var page = FetchPage(itemUrl, context);
                return ResolveItem(page.Url, page.Html, folder, sourceUrl, albumDate, context);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (FileHostException ex)
            {
                context.Error(ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                // Runs on a worker thread: an escaping exception would take XDM down with it.
                context.Error($"{itemUrl}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <param name="albumDate">Upload date read from the album listing, which is more reliable
        /// than the item page; null for single file links.</param>
        private ResolvedFile? ResolveItem(string itemUrl, string html, string folder, string sourceUrl, DateTime? albumDate, FileHostContext context)
        {
            var downloadLink = GetDownloadLink(itemUrl, html, context);
            var pageName = BunkrHtml.GetItemFileName(html);
            var displayName = pageName ?? BunkrHtml.GetMediaSlug(html) ?? LastPathSegment(itemUrl);

            if (downloadLink == null)
            {
                context.Error(string.Format(TextResource.GetText("FH_BK_UNRESOLVED"), displayName));
                return null;
            }

            var urlName = LastPathSegment(downloadLink);
            var name = pageName == null ? urlName
                : options.BunkrCleanName ? pageName
                : FormatItemFileName(pageName, urlName);
            name = FileHostNames.Sanitize(name, FileHostNames.Sanitize(urlName, "bunkr-file"));

            if (IsFilteredOut(name))
            {
                context.Info(string.Format(TextResource.GetText("FH_BK_FILTERED"), name));
                return null;
            }

            WarnIfServerOffline(downloadLink, name, context);

            return new ResolvedFile
            {
                Host = Name,
                SourceUrl = sourceUrl,
                Name = name,
                RelativeFolder = folder,
                Url = downloadLink,
                LastModified = albumDate ?? BunkrHtml.GetItemDate(html),
                Headers = new Dictionary<string, List<string>>
                {
                    ["User-Agent"] = new() { UserAgent },
                    ["Referer"] = new() { DownloadReferer }
                }
            };
        }

        /// <summary>
        /// CDN base from the page variables (jsCDN), or the download API for assets without a
        /// landing page, then a token from the signing API.
        /// </summary>
        private string? GetDownloadLink(string itemUrl, string html, FileHostContext context)
        {
            var variables = BunkrHtml.GetPageVariables(html);
            variables.TryGetValue("jsCDN", out var cdnValue);
            var cdnUrl = ToAbsoluteUrl(cdnValue);

            string? unsignedUrl = null;
            if (variables.Count == 0 && BunkrHtml.GetFileId(html) is string fileId)
            {
                unsignedUrl = GetUnsignedUrl(fileId, context);
            }

            if (cdnUrl == null && unsignedUrl == null)
            {
                return null;
            }

            var baseUrl = cdnUrl ?? unsignedUrl!;
            var mediaPath = cdnUrl != null
                ? new Uri(cdnUrl).AbsolutePath
                : "/storage/media/" + LastPathSegment(unsignedUrl ?? itemUrl, decode: false);

            FileHostHttpResponse response;
            try
            {
                response = FileHostRequest.Send(http, context, options, "GET",
                    SignApi + "?path=" + Uri.EscapeDataString(mediaPath), JsonHeaders());
            }
            catch (FileHostException ex)
            {
                context.Warn(ex.Message);
                return null;
            }

            var payload = TryParseJson(response);
            var token = payload?["token"]?.ToString();
            var expires = payload?["ex"]?.ToString();

            if (!response.IsSuccess || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expires))
            {
                // Without a token the plain CDN link is still worth a try.
                return cdnUrl;
            }

            var separator = baseUrl.Contains("?") ? "&" : "?";
            return $"{baseUrl}{separator}token={Uri.EscapeDataString(token)}&ex={Uri.EscapeDataString(expires)}";
        }

        private string? GetUnsignedUrl(string fileId, FileHostContext context)
        {
            var body = Encoding.UTF8.GetBytes(new JObject { ["id"] = fileId }.ToString(Formatting.None));
            var headers = JsonHeaders();
            headers["Content-Type"] = "application/json";

            FileHostHttpResponse response;
            try
            {
                response = FileHostRequest.Send(http, context, options, "POST", DownloadApi, headers, body);
            }
            catch (FileHostException ex)
            {
                context.Warn(ex.Message);
                return null;
            }

            var payload = response.IsSuccess ? TryParseJson(response) : null;
            var mediaFiles = payload?["mediafiles"]?.ToString();
            var path = payload?["path"]?.ToString();
            if (string.IsNullOrEmpty(mediaFiles) || string.IsNullOrEmpty(path)
                || !Uri.TryCreate(mediaFiles, UriKind.Absolute, out var mediaUri))
            {
                return null;
            }
            return new UriBuilder(mediaUri) { Path = path }.Uri.AbsoluteUri;
        }

        private class Page
        {
            public Page(string url, string html)
            {
                Url = url;
                Html = html;
            }

            public string Url { get; }

            public string Html { get; }
        }

        /// <summary>
        /// Loads a page, switching once to the fallback domain when the mirror answers 403.
        /// </summary>
        private Page FetchPage(string url, FileHostContext context)
        {
            var current = url;
            var triedFallback = false;
            while (true)
            {
                var response = FileHostRequest.Send(http, context, options, "GET", current, PageHeaders());
                if (response.StatusCode == 403 && !triedFallback)
                {
                    triedFallback = true;
                    current = new UriBuilder(new Uri(current)) { Host = FallbackDomain, Port = -1 }.Uri.AbsoluteUri;
                    continue;
                }
                if (!response.IsSuccess)
                {
                    throw new FileHostException(string.Format(TextResource.GetText("FH_BK_PAGE_FAILED"), current, response.StatusCode));
                }
                return new Page(response.ResponseUri?.AbsoluteUri ?? current, response.Body);
            }
        }

        private void WarnIfServerOffline(string downloadLink, string name, FileHostContext context)
        {
            if (!Uri.TryCreate(downloadLink, UriKind.Absolute, out var uri))
            {
                return;
            }
            var status = GetServerStatus(context);
            var server = uri.Host.Split('.')[0];
            if (status.TryGetValue(server, out var state) && !string.Equals(state, "Operational", StringComparison.OrdinalIgnoreCase))
            {
                context.Warn(string.Format(TextResource.GetText("FH_BK_OFFLINE"), server, name));
            }
        }

        private Dictionary<string, string> GetServerStatus(FileHostContext context)
        {
            lock (this)
            {
                if (serverStatus != null)
                {
                    return serverStatus;
                }
                try
                {
                    var response = http.Send("GET", StatusPage, PageHeaders());
                    serverStatus = response.IsSuccess ? BunkrHtml.GetServerStatus(response.Body) : new Dictionary<string, string>();
                }
                catch (FileHostException)
                {
                    // The status page is only used for warnings; carry on without it.
                    serverStatus = new Dictionary<string, string>();
                }
                return serverStatus;
            }
        }

        private bool IsFilteredOut(string name)
        {
            if (options.BunkrIgnore.Any(word => word.Length > 0 && name.Contains(word)))
            {
                return true;
            }
            var include = options.BunkrInclude.Where(word => word.Length > 0).ToList();
            return include.Count > 0 && !include.Any(word => name.Contains(word));
        }

        /// <summary>
        /// Combines the page title with the CDN name, keeping the extension of the title.
        /// Same rules as the original crawler: identical names stay, a CDN name that is the title
        /// or starts with "title-" / "title_" is kept, anything else becomes "title-cdnname.ext".
        /// </summary>
        public static string FormatItemFileName(string originalName, string urlName)
        {
            if (originalName == urlName)
            {
                return originalName;
            }

            FileHostNames.SplitExtension(originalName, out var originalBase, out var extension);
            FileHostNames.SplitExtension(urlName, out var urlBase, out _);

            if (urlBase == originalBase
                || urlBase.StartsWith(originalBase + "-", StringComparison.Ordinal)
                || urlBase.StartsWith(originalBase + "_", StringComparison.Ordinal))
            {
                return urlName;
            }

            var validBase = Regex.Replace(originalBase, @"[<>:""/\\|?*\x00-\x1f]", string.Empty);
            return $"{validBase}-{urlBase}{extension}";
        }

        internal static string? GetUrlType(Uri uri)
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2 || segments[segments.Length - 1].Length == 0)
            {
                return null;
            }
            var type = segments[segments.Length - 2].ToLowerInvariant();
            return AlbumTypes.Contains(type) || FileTypes.Contains(type) ? type : null;
        }

        private static bool TryParse(string url, out Uri? uri)
        {
            return Uri.TryCreate(FileHostLinkParser.EnsureScheme(url ?? string.Empty), UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static string RemovePageParameter(Uri uri)
        {
            var query = uri.Query.TrimStart('?')
                .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("page=", StringComparison.OrdinalIgnoreCase) && p != "page")
                .ToList();
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1, Query = string.Join("&", query), Fragment = string.Empty };
            return builder.Uri.AbsoluteUri;
        }

        private static string? ToAbsoluteUrl(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            var candidate = value!.StartsWith("//") ? "https:" + value : value;
            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? candidate : null;
        }

        private static string LastPathSegment(string url, bool decode = true)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            var segment = path.TrimEnd('/');
            segment = segment.Substring(segment.LastIndexOf('/') + 1);
            return decode ? Uri.UnescapeDataString(segment) : segment;
        }

        private static JObject? TryParseJson(FileHostHttpResponse response)
        {
            try
            {
                return JToken.Parse(response.Body) as JObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static Dictionary<string, string> PageHeaders() => new()
        {
            ["User-Agent"] = UserAgent,
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        };

        private static Dictionary<string, string> JsonHeaders() => new()
        {
            ["User-Agent"] = UserAgent,
            ["Accept"] = "application/json, text/plain, */*"
        };
    }
}
