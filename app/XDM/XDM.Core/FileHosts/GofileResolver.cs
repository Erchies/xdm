using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Translations;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Lists gofile.io folders through the site API. Port of the fixed core of
    /// gofile-downloader-gui (derived from ltsdw/gofile-downloader).
    /// </summary>
    public class GofileResolver : IFileHostResolver
    {
        public const string ApiBase = "https://api.gofile.io";

        /// <summary>
        /// Salt taken from the site javascript. GoFile rotates it now and then, so it can be
        /// overridden in the options without a rebuild.
        /// </summary>
        public const string DefaultWebsiteTokenSalt = "12af056dacea0b";

        public const string DefaultUserAgent = "Mozilla/5.0";

        private const long TokenSlotSeconds = 14400;

        private static readonly Regex LinkRegex = new(@"gofile\.io/(?:d|w)/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        private static readonly Regex BareIdRegex = new(@"^[A-Za-z0-9]{4,}$");

        private readonly IFileHostHttp http;
        private readonly FileHostOptions options;
        private readonly Func<long> unixTimeSeconds;
        private string token = string.Empty;

        public GofileResolver(IFileHostHttp http, FileHostOptions options, Func<long>? unixTimeSeconds = null)
        {
            this.http = http;
            this.options = options;
            this.unixTimeSeconds = unixTimeSeconds ?? (() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
        }

        public string Name => "GoFile";

        private string UserAgent => string.IsNullOrEmpty(options.GofileUserAgent) ? DefaultUserAgent : options.GofileUserAgent;

        private string Salt => string.IsNullOrEmpty(options.GofileWebsiteTokenSalt) ? DefaultWebsiteTokenSalt : options.GofileWebsiteTokenSalt;

        public bool CanHandle(string url) => IsGofileLink(url);

        public static bool IsGofileLink(string url) => LinkRegex.IsMatch(url ?? string.Empty);

        /// <summary>
        /// X-Website-Token required by the API: sha256 of the user agent, language, account
        /// token, 4 hour time slot and salt.
        /// </summary>
        public static string GenerateWebsiteToken(string userAgent, string accountToken, string salt,
            long unixSeconds, int slotOffset = 0)
        {
            var slot = unixSeconds / TokenSlotSeconds + slotOffset;
            return Sha256Hex($"{userAgent}::en-US::{accountToken}::{slot}::{salt}");
        }

        public static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        public static string ParseContentId(string url)
        {
            var cleaned = (url ?? string.Empty).Trim();
            cleaned = cleaned.Split('?')[0].Split('#')[0].TrimEnd('/');

            var match = LinkRegex.Match(cleaned);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            if (BareIdRegex.IsMatch(cleaned))
            {
                return cleaned;
            }
            throw new FileHostException(string.Format(TextResource.GetText("FH_INVALID_URL"), url));
        }

        public IList<ResolvedFile> Resolve(FileHostLink link, FileHostContext context)
        {
            var contentId = ParseContentId(link.Url);
            if (string.IsNullOrEmpty(token))
            {
                Authenticate(context);
            }

            var hashedPassword = string.IsNullOrEmpty(link.Password) ? null : Sha256Hex(link.Password!);
            var root = options.CreateSubfolders ? FileHostNames.Sanitize(contentId, "gofile") : string.Empty;
            var files = new List<ResolvedFile>();
            var names = new FileHostNameRegistry();

            Walk(contentId, root, hashedPassword, link.Url, files, names, context, isRoot: true);
            return files;
        }

        private void Authenticate(FileHostContext context)
        {
            if (!string.IsNullOrEmpty(options.GofileAccountToken))
            {
                token = options.GofileAccountToken.Trim();
                context.Info(TextResource.GetText("FH_GF_USING_TOKEN"));
                return;
            }

            var response = FileHostRequest.Send(http, context, options, "POST", ApiBase + "/accounts", ApiHeaders(0));
            var payload = ParseJson(response);
            var status = (string?)payload["status"];

            if (status != "ok")
            {
                throw new FileHostException(string.Format(TextResource.GetText("FH_GF_ACCOUNT_FAILED"), response.StatusCode, status));
            }

            var newToken = (string?)payload["data"]?["token"];
            if (string.IsNullOrEmpty(newToken))
            {
                throw new FileHostException(TextResource.GetText("FH_GF_NO_TOKEN"));
            }

            token = newToken!;
            context.Info(TextResource.GetText("FH_GF_GUEST_CREATED"));
        }

        private void Walk(string contentId, string folder, string? hashedPassword, string sourceUrl,
            List<ResolvedFile> files, FileHostNameRegistry names, FileHostContext context, bool isRoot = false)
        {
            context.ThrowIfCancelled();

            var data = FetchContent(contentId, hashedPassword, context);

            var passwordStatus = (string?)data["passwordStatus"];
            if (!string.IsNullOrEmpty(passwordStatus) && passwordStatus != "passwordOk")
            {
                throw new FileHostPasswordException(string.Format(TextResource.GetText("FH_PASSWORD_REQUIRED"), contentId));
            }

            if ((string?)data["type"] != "folder")
            {
                AddFile(data, folder, sourceUrl, files, names, context);
                return;
            }

            var folderPath = isRoot
                ? folder
                : FileHostNames.JoinRelative(folder, names.Reserve(folder, FileHostNames.Sanitize((string?)data["name"]), isFolder: true));

            if (data["children"] is not JObject children)
            {
                return;
            }

            foreach (var property in children.Properties())
            {
                context.ThrowIfCancelled();
                if (property.Value is not JObject child)
                {
                    continue;
                }

                if ((string?)child["type"] == "folder")
                {
                    var childId = (string?)child["id"];
                    if (!string.IsNullOrEmpty(childId))
                    {
                        Walk(childId!, folderPath, hashedPassword, sourceUrl, files, names, context);
                    }
                }
                else
                {
                    AddFile(child, folderPath, sourceUrl, files, names, context);
                }
            }
        }

        private JObject FetchContent(string contentId, string? hashedPassword, FileHostContext context)
        {
            var url = $"{ApiBase}/contents/{Uri.EscapeDataString(contentId)}?cache=true&sortField=createTime&sortDirection=1";
            if (hashedPassword != null)
            {
                url += "&password=" + hashedPassword;
            }

            string? status = null;
            // A request right at the turn of the 4 hour slot can be signed with the previous one.
            foreach (var slotOffset in new[] { 0, -1 })
            {
                var response = FileHostRequest.Send(http, context, options, "GET", url, ApiHeaders(slotOffset));
                var payload = ParseJson(response);
                status = (string?)payload["status"];

                switch (status)
                {
                    case "ok":
                        return payload["data"] as JObject ?? new JObject();
                    case "error-passwordRequired":
                        throw new FileHostPasswordException(string.Format(TextResource.GetText("FH_PASSWORD_REQUIRED"), contentId));
                    case "error-passwordWrong":
                        throw new FileHostPasswordException(string.Format(TextResource.GetText("FH_PASSWORD_WRONG"), contentId));
                    case "error-notFound":
                        throw new FileHostException(string.Format(TextResource.GetText("FH_NOT_FOUND"), contentId));
                }
            }

            throw new FileHostException(string.Format(TextResource.GetText("FH_GF_REFUSED"), contentId, status));
        }

        private void AddFile(JObject node, string folder, string sourceUrl, List<ResolvedFile> files,
            FileHostNameRegistry names, FileHostContext context)
        {
            var link = (string?)node["link"];
            var rawName = (string?)node["name"];
            if (string.IsNullOrEmpty(link))
            {
                context.Warn(string.Format(TextResource.GetText("FH_NO_LINK"), rawName));
                return;
            }

            var size = node["size"];
            files.Add(new ResolvedFile
            {
                Host = Name,
                SourceUrl = sourceUrl,
                Name = names.Reserve(folder, FileHostNames.Sanitize(rawName)),
                RelativeFolder = folder,
                Url = link!,
                Size = size != null && (size.Type == JTokenType.Integer || size.Type == JTokenType.Float) ? (long)size : -1,
                Headers = DownloadHeaders(),
                Cookies = "accountToken=" + token
            });
        }

        private Dictionary<string, string> ApiHeaders(int slotOffset)
        {
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = UserAgent,
                ["Accept"] = "*/*",
                ["Origin"] = "https://gofile.io",
                ["Referer"] = "https://gofile.io/",
                ["X-Website-Token"] = GenerateWebsiteToken(UserAgent, token, Salt, unixTimeSeconds(), slotOffset),
                ["X-BL"] = "en-US"
            };
            if (!string.IsNullOrEmpty(token))
            {
                headers["Authorization"] = "Bearer " + token;
                headers["Cookie"] = "accountToken=" + token;
            }
            return headers;
        }

        private Dictionary<string, List<string>> DownloadHeaders()
        {
            return new Dictionary<string, List<string>>
            {
                ["User-Agent"] = new() { UserAgent },
                ["Accept"] = new() { "*/*" },
                ["Origin"] = new() { "https://gofile.io" },
                ["Referer"] = new() { "https://gofile.io/" },
                ["Authorization"] = new() { "Bearer " + token }
            };
        }

        private static JObject ParseJson(FileHostHttpResponse response)
        {
            try
            {
                if (JToken.Parse(response.Body) is JObject obj)
                {
                    return obj;
                }
            }
            catch (JsonException)
            {
            }

            var snippet = Regex.Replace(response.Body.Length > 200 ? response.Body.Substring(0, 200) : response.Body, @"\s+", " ");
            throw new FileHostException(string.Format(TextResource.GetText("FH_NOT_JSON"), response.StatusCode, snippet));
        }
    }
}
