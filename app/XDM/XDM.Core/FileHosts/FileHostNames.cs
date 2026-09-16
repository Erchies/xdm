using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XDM.Core.FileHosts
{
    public static class FileHostNames
    {
        public const int MaxNameLength = 150;

        private static readonly Regex IllegalChars = new(@"[<>:""/\\|?*\x00-\x1f]");

        private static readonly HashSet<string> WindowsReserved = new(
            new[] { "CON", "PRN", "AUX", "NUL" }
                .Concat(Enumerable.Range(1, 9).Select(i => "COM" + i))
                .Concat(Enumerable.Range(1, 9).Select(i => "LPT" + i)),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Turns a name coming from a host into a safe file or folder name: drops any path
        /// component (defence against path traversal), replaces characters Windows rejects,
        /// avoids reserved device names and caps the length.
        /// </summary>
        public static string Sanitize(string? raw, string fallback = "unnamed")
        {
            var name = (raw ?? string.Empty).Normalize(NormalizationForm.FormC);
            name = name.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            name = IllegalChars.Replace(name, "_").Trim();

            if (name.Length == 0 || name == "." || name == "..")
            {
                return fallback;
            }

            // Windows does not accept names ending with a dot or a space.
            name = name.TrimEnd(' ', '.');
            if (name.Length == 0)
            {
                return fallback;
            }

            var dot = name.IndexOf('.');
            var stem = dot < 0 ? name : name.Substring(0, dot);
            if (WindowsReserved.Contains(stem))
            {
                name = "_" + name;
            }

            if (name.Length > MaxNameLength)
            {
                SplitExtension(name, out var root, out var extension);
                if (extension.Length >= MaxNameLength)
                {
                    extension = string.Empty;
                }
                name = root.Substring(0, Math.Max(1, Math.Min(root.Length, MaxNameLength - extension.Length))) + extension;
            }

            return name;
        }

        /// <summary>
        /// Splits "name.ext" into "name" and ".ext". Unlike Path.GetExtension it never throws
        /// on characters that are invalid in paths.
        /// </summary>
        public static void SplitExtension(string name, out string root, out string extension)
        {
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                root = name;
                extension = string.Empty;
                return;
            }
            root = name.Substring(0, dot);
            extension = name.Substring(dot);
        }

        /// <summary>
        /// Joins sanitized path components into a relative folder.
        /// </summary>
        public static string JoinRelative(string parent, string child)
        {
            if (string.IsNullOrEmpty(parent))
            {
                return child;
            }
            return string.IsNullOrEmpty(child) ? parent : parent + Path.DirectorySeparatorChar + child;
        }

        /// <summary>
        /// Resolves the absolute folder for a relative folder, refusing anything that would
        /// land outside the download location.
        /// </summary>
        public static string CombineWithin(string root, string relativeFolder)
        {
            var fullRoot = Path.GetFullPath(root);
            if (string.IsNullOrEmpty(relativeFolder))
            {
                return fullRoot;
            }

            var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativeFolder));
            var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? fullRoot : fullRoot + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new FileHostException("Refused a folder outside the download location: " + candidate);
            }
            return candidate;
        }
    }

    /// <summary>
    /// Hands out names that do not collide inside the same folder, adding "(1)", "(2)"...
    /// Comparison ignores case because the Windows file system does.
    /// </summary>
    public class FileHostNameRegistry
    {
        private readonly HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        public string Reserve(string relativeFolder, string name, bool isFolder = false)
        {
            var candidate = name;
            for (var i = 1; !used.Add(FileHostNames.JoinRelative(relativeFolder, candidate)); i++)
            {
                if (isFolder)
                {
                    candidate = $"{name}({i})";
                }
                else
                {
                    FileHostNames.SplitExtension(name, out var root, out var extension);
                    candidate = $"{root}({i}){extension}";
                }
            }
            return candidate;
        }
    }

    public static class FileHostLinkParser
    {
        /// <summary>
        /// Reads one link per line. Anything after the first blank on a line is that link's
        /// password; lines without one use <paramref name="defaultPassword"/>.
        /// Empty lines and lines starting with '#' are ignored, repeated links are dropped.
        /// </summary>
        public static List<FileHostLink> Parse(string? text, string? defaultPassword = null)
        {
            var links = new List<FileHostLink>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text))
            {
                return links;
            }

            foreach (var rawLine in text!.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var url = parts[0];
                var password = parts.Length > 1 ? parts[1].Trim() : defaultPassword;
                if (seen.Add(url))
                {
                    links.Add(new FileHostLink(url, string.IsNullOrEmpty(password) ? null : password));
                }
            }
            return links;
        }

        /// <summary>
        /// Adds https:// to links pasted without a scheme.
        /// </summary>
        public static string EnsureScheme(string url)
        {
            var trimmed = url.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            return "https://" + trimmed;
        }
    }
}
