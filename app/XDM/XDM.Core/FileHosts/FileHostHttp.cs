using System;
using System.Collections.Generic;
using Translations;

namespace XDM.Core.FileHosts
{
    public class FileHostHttpResponse
    {
        public FileHostHttpResponse(int statusCode, string body, Uri? responseUri = null)
        {
            StatusCode = statusCode;
            Body = body;
            ResponseUri = responseUri;
        }

        public int StatusCode { get; }

        public string Body { get; }

        public Uri? ResponseUri { get; }

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>
    /// Minimal HTTP abstraction used by the resolvers. Unlike XDM's download clients it must
    /// return the body of error responses too, because the host APIs explain failures there.
    /// </summary>
    public interface IFileHostHttp
    {
        /// <exception cref="FileHostNetworkException">No HTTP response was received.</exception>
        FileHostHttpResponse Send(string method, string url,
            IDictionary<string, string>? headers = null, byte[]? body = null);
    }

    internal static class FileHostRequest
    {
        private const int MaxDelayMilliseconds = 15000;

        /// <summary>
        /// Sends a request retrying connection failures, 429 and 5xx with exponential backoff.
        /// Any other status is returned to the caller as is.
        /// </summary>
        internal static FileHostHttpResponse Send(IFileHostHttp http, FileHostContext context,
            FileHostOptions options, string method, string url,
            IDictionary<string, string>? headers = null, byte[]? body = null)
        {
            var retries = Math.Max(1, options.Retries);
            var lastError = string.Empty;

            for (var attempt = 1; attempt <= retries; attempt++)
            {
                context.ThrowIfCancelled();
                try
                {
                    var response = http.Send(method, url, headers, body);
                    if (!IsTransient(response.StatusCode) || attempt == retries)
                    {
                        return response;
                    }
                    lastError = "HTTP " + response.StatusCode;
                }
                catch (FileHostNetworkException ex)
                {
                    lastError = ex.Message;
                    if (attempt == retries)
                    {
                        break;
                    }
                }

                context.Warn(string.Format(TextResource.GetText("FH_RETRY"), attempt, retries, url, lastError));
                if (!context.Delay(BackoffDelay(options, attempt)))
                {
                    throw new OperationCanceledException();
                }
            }

            throw new FileHostException(string.Format(TextResource.GetText("FH_GAVE_UP"), url, retries, lastError));
        }

        internal static bool IsTransient(int statusCode) => statusCode == 429 || statusCode >= 500;

        private static int BackoffDelay(FileHostOptions options, int attempt)
        {
            var delay = options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt - 1);
            return (int)Math.Min(delay, MaxDelayMilliseconds);
        }
    }
}
