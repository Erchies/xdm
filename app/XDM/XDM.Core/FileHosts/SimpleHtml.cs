using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Just enough HTML scanning for the file host resolvers: find opening tags, read their
    /// attributes and the text between them. A handful of targeted patterns replace a full
    /// HTML parser, which would otherwise be a dependency of every XDM front end.
    /// </summary>
    public static class SimpleHtml
    {
        // Quoted attribute values may contain '>' (Alpine.js handlers such as "() => open = true").
        private static readonly Regex OpenTagRegex = new(@"<([a-zA-Z][a-zA-Z0-9]*)\b((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Singleline);
        private static readonly Regex RawTextRegex = new(@"(<(script|style)\b[^>]*>)(.*?)(</\2\s*>)|<!--.*?-->", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex AttributeRegex = new(@"([^\s=/>""']+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+)))?", RegexOptions.Singleline);
        private static readonly Regex TagStripRegex = new(@"<[^>]*>", RegexOptions.Singleline);

        public class HtmlTag
        {
            public HtmlTag(string name, Dictionary<string, string> attributes, int start, int end)
            {
                Name = name;
                Attributes = attributes;
                Start = start;
                End = end;
            }

            public string Name { get; }

            public Dictionary<string, string> Attributes { get; }

            /// <summary>Index of '&lt;'.</summary>
            public int Start { get; }

            /// <summary>Index right after the '&gt;' of the opening tag.</summary>
            public int End { get; }

            public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

            public bool HasClasses(IEnumerable<string> classes)
            {
                var own = (Attribute("class") ?? string.Empty)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return classes.All(c => own.Contains(c));
            }
        }

        /// <param name="tagName">Tag to look for, or null for every tag.</param>
        public static IEnumerable<HtmlTag> FindTags(string html, string? tagName, int start = 0)
        {
            // Script bodies and comments are blanked out (keeping offsets) so that code such as
            // "i<n" is not mistaken for a tag.
            var masked = MaskRawText(html);
            for (var match = OpenTagRegex.Match(masked, start); match.Success; match = match.NextMatch())
            {
                if (tagName != null && !string.Equals(match.Groups[1].Value, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                yield return new HtmlTag(match.Groups[1].Value.ToLowerInvariant(),
                    ParseAttributes(match.Groups[2].Value), match.Index, match.Index + match.Length);
            }
        }

        private static string MaskRawText(string html)
        {
            return RawTextRegex.Replace(html, m => m.Groups[1].Success
                ? m.Groups[1].Value + new string(' ', m.Groups[3].Length) + m.Groups[4].Value
                : new string(' ', m.Length));
        }

        public static Dictionary<string, string> ParseAttributes(string text)
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in AttributeRegex.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (attributes.ContainsKey(name))
                {
                    continue;
                }
                var value = match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Success ? match.Groups[3].Value
                    : match.Groups[4].Value;
                attributes[name] = WebUtility.HtmlDecode(value);
            }
            return attributes;
        }

        /// <summary>
        /// Markup between the opening tag and its closing tag. Nested tags of the same name
        /// are not balanced, which is fine for the h1/nav/p/span elements read here.
        /// </summary>
        public static string InnerHtml(string html, HtmlTag tag)
        {
            var close = html.IndexOf("</" + tag.Name, tag.End, StringComparison.OrdinalIgnoreCase);
            return close < 0 ? html.Substring(tag.End) : html.Substring(tag.End, close - tag.End);
        }

        /// <summary>
        /// Visible text of a fragment. Pass " " as <paramref name="tagReplacement"/> to keep
        /// adjacent elements apart, so that "&lt;a&gt;1&lt;/a&gt;&lt;a&gt;2&lt;/a&gt;" does not read as "12".
        /// </summary>
        public static string Text(string fragment, string tagReplacement = "")
        {
            return WebUtility.HtmlDecode(TagStripRegex.Replace(fragment, tagReplacement)).Trim();
        }
    }
}
