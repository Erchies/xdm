using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace XDM.Core
{
    public enum DuplicateReason
    {
        None,

        /// <summary>A file with this name is already in the destination folder.</summary>
        FileExists,

        /// <summary>An unfinished download in XDM already saves to the same file.</summary>
        AlreadyInList,

        /// <summary>The same file appears earlier in the links being added now.</summary>
        RepeatedInBatch
    }

    /// <summary>
    /// Decides whether a download would fetch a file that is already there, by destination path:
    /// links carrying tokens (Bunkr, GoFile) change every time, the file name does not. Create one
    /// per batch of links so repeats inside the batch are caught too.
    /// </summary>
    public class DuplicateDownloadChecker
    {
        private readonly Func<string, string> defaultFolderForName;
        private readonly Func<string, bool> fileExists;
        private readonly HashSet<string> inList = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> inBatch = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="unfinished">Folder (null when chosen by file type) and name of each unfinished download.</param>
        /// <param name="defaultFolderForName">Folder XDM picks for a name when none is given.</param>
        /// <param name="fileExists">File.Exists, replaceable in tests.</param>
        public DuplicateDownloadChecker(IEnumerable<KeyValuePair<string?, string>> unfinished,
            Func<string, string> defaultFolderForName, Func<string, bool>? fileExists = null)
        {
            this.defaultFolderForName = defaultFolderForName;
            this.fileExists = fileExists ?? File.Exists;
            foreach (var download in unfinished)
            {
                if (TargetPath(download.Key, download.Value) is string path)
                {
                    inList.Add(path);
                }
            }
        }

        /// <summary>
        /// Checks one download and, when it is not a duplicate, remembers it for the rest of the batch.
        /// </summary>
        /// <param name="path">Full destination path, when it could be worked out.</param>
        public DuplicateReason Check(string name, string? folder, out string? path)
        {
            path = TargetPath(folder, name);
            if (path == null)
            {
                return DuplicateReason.None;
            }
            if (fileExists(path))
            {
                return DuplicateReason.FileExists;
            }
            if (inList.Contains(path))
            {
                return DuplicateReason.AlreadyInList;
            }
            return inBatch.Add(path) ? DuplicateReason.None : DuplicateReason.RepeatedInBatch;
        }

        private string? TargetPath(string? folder, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            try
            {
                var directory = string.IsNullOrEmpty(folder) ? defaultFolderForName(name) : folder!;
                return string.IsNullOrEmpty(directory) ? null : Path.GetFullPath(Path.Combine(directory, name));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Text file listing the downloads that were skipped as duplicates, one per line:
    /// date, reason, destination and link, separated by tabs.
    /// </summary>
    public static class SkippedDownloadList
    {
        public const string FileName = "skipped-downloads.txt";

        public static void Append(string listPath, DateTime when, string reason, string destination, string? url)
        {
            var line = string.Join("\t",
                when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Clean(reason), Clean(destination), Clean(url)) + Environment.NewLine;
            // A byte order mark on the first write keeps accents readable in any editor.
            File.AppendAllText(listPath, line, new UTF8Encoding(true));
        }

        private static string Clean(string? value) =>
            (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
