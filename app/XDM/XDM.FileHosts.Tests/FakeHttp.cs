using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XDM.Core.FileHosts;

namespace XDM.FileHosts.Tests
{
    internal class FakeRequest
    {
        public FakeRequest(string method, string url, IDictionary<string, string>? headers, byte[]? body)
        {
            Method = method;
            Url = url;
            Headers = headers == null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers);
            Body = body == null ? null : Encoding.UTF8.GetString(body);
        }

        public string Method { get; }
        public string Url { get; }
        public Dictionary<string, string> Headers { get; }
        public string? Body { get; }
    }

    /// <summary>
    /// Answers requests from registered routes; the most recently added matching route wins.
    /// </summary>
    internal class FakeHttp : IFileHostHttp
    {
        private readonly List<(string Method, Func<string, bool> Match, Func<FakeRequest, FileHostHttpResponse> Respond)> routes = new();

        public List<FakeRequest> Requests { get; } = new();

        public FakeHttp On(string method, string urlPrefix, int status, string body)
        {
            return On(method, url => url.StartsWith(urlPrefix, StringComparison.Ordinal), _ => new FileHostHttpResponse(status, body));
        }

        public FakeHttp On(string method, Func<string, bool> match, Func<FakeRequest, FileHostHttpResponse> respond)
        {
            routes.Add((method, match, respond));
            return this;
        }

        public IEnumerable<FakeRequest> RequestsTo(string urlPrefix) =>
            Requests.Where(r => r.Url.StartsWith(urlPrefix, StringComparison.Ordinal));

        public FileHostHttpResponse Send(string method, string url, IDictionary<string, string>? headers = null, byte[]? body = null)
        {
            var request = new FakeRequest(method, url, headers, body);
            lock (Requests)
            {
                Requests.Add(request);
            }
            for (var i = routes.Count - 1; i >= 0; i--)
            {
                if (routes[i].Method == method && routes[i].Match(url))
                {
                    return routes[i].Respond(request);
                }
            }
            return new FileHostHttpResponse(404, "not found: " + url);
        }
    }
}
