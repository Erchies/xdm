using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using XDM.Core.Clients.Http;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// HttpWebRequest based client for the resolvers. Honours the proxy configured in XDM.
    /// </summary>
    internal class FileHostWebClient : IFileHostHttp
    {
        private readonly ProxyInfo? proxy;
        private readonly int timeoutMilliseconds;

        internal FileHostWebClient(ProxyInfo? proxy, int timeoutSeconds)
        {
            this.proxy = proxy;
            this.timeoutMilliseconds = Math.Max(5, timeoutSeconds) * 1000;
        }

        public FileHostHttpResponse Send(string method, string url,
            IDictionary<string, string>? headers = null, byte[]? body = null)
        {
            HttpWebRequest request;
            try
            {
#pragma warning disable SYSLIB0014 // HttpWebRequest is what the .NET Framework build has.
                request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            }
            catch (Exception ex) when (ex is UriFormatException || ex is NotSupportedException)
            {
                throw new FileHostException(url + ": " + ex.Message, ex);
            }

            request.Method = method;
            request.Timeout = request.ReadWriteTimeout = timeoutMilliseconds;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            var webProxy = ProxyHelper.GetProxy(proxy);
            if (webProxy != null)
            {
                request.Proxy = webProxy;
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    SetHeader(request, header.Key, header.Value);
                }
            }

            try
            {
                if (body != null)
                {
                    request.ContentLength = body.Length;
                    using var stream = request.GetRequestStream();
                    stream.Write(body, 0, body.Length);
                }

                HttpWebResponse response;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException we) when (we.Response is HttpWebResponse errorResponse)
                {
                    // 4xx/5xx still carry a body worth reading (API status, error page).
                    response = errorResponse;
                }

                using (response)
                {
                    return new FileHostHttpResponse((int)response.StatusCode, ReadBody(response), response.ResponseUri);
                }
            }
            catch (WebException we)
            {
                throw new FileHostNetworkException(we.Message, we);
            }
            catch (IOException ioe)
            {
                throw new FileHostNetworkException(ioe.Message, ioe);
            }
        }

        private static string ReadBody(HttpWebResponse response)
        {
            using var stream = response.GetResponseStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            // HttpWebResponse.CharacterSet reports ISO-8859-1 for any text/* response without an
            // explicit charset, which garbles UTF-8 pages. Only trust a declared charset.
            var encoding = Encoding.UTF8;
            var charset = Regex.Match(response.ContentType ?? string.Empty, @"charset\s*=\s*""?([^;""\s]+)", RegexOptions.IgnoreCase);
            if (charset.Success)
            {
                try
                {
                    encoding = Encoding.GetEncoding(charset.Groups[1].Value);
                }
                catch (ArgumentException)
                {
                }
            }
            return encoding.GetString(buffer.ToArray());
        }

        private static void SetHeader(HttpWebRequest request, string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "user-agent":
                    request.UserAgent = value;
                    break;
                case "referer":
                    request.Referer = value;
                    break;
                case "accept":
                    request.Accept = value;
                    break;
                case "content-type":
                    request.ContentType = value;
                    break;
                case "connection":
                case "content-length":
                case "host":
                    break;
                default:
                    request.Headers[key] = value;
                    break;
            }
        }
    }
}
