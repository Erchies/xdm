using System.Collections.Generic;
using System.Linq;

namespace XDM.Core.FileHosts
{
    public static class FileHostResolvers
    {
        public static bool IsSupported(string? url)
        {
            return !string.IsNullOrEmpty(url) && (GofileResolver.IsGofileLink(url!) || BunkrResolver.IsBunkrLink(url!));
        }

        /// <summary>
        /// One resolver per host. Create a new set per operation: the GoFile resolver keeps the
        /// guest token it obtained so several links share a single account.
        /// </summary>
        public static IList<IFileHostResolver> Create(IFileHostHttp http, FileHostOptions options)
        {
            return new List<IFileHostResolver>
            {
                new GofileResolver(http, options),
                new BunkrResolver(http, options)
            };
        }

        public static IFileHostResolver? Find(IEnumerable<IFileHostResolver> resolvers, string url)
        {
            return resolvers.FirstOrDefault(r => r.CanHandle(url));
        }

        /// <summary>
        /// Splits a filter typed as "word1, word2; word3" into its words.
        /// </summary>
        public static List<string> ParseFilter(string? text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => w.Length > 0)
                .ToList();
        }
    }
}
