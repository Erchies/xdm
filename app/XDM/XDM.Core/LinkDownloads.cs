using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TraceLog;
using Translations;
using XDM.Core.DataAccess;
using XDM.Core.Util;

namespace XDM.Core
{
    /// <summary>
    /// Options for downloads the user adds by pasting links (new download, batch lists, GoFile /
    /// Bunkr, video links, links copied to the clipboard): opening them without progress or
    /// completion windows, and skipping files that were already downloaded.
    /// </summary>
    public static class LinkDownloads
    {
        private const string QuietMarkerExtension = ".quiet";

        /// <summary>
        /// Whether a download added from this message should open no windows. Messages that
        /// carry browser headers or cookies come from the browser integration and keep the
        /// usual windows; a bare link (clipboard, typed) counts as a link download.
        /// </summary>
        public static bool IsQuiet(Message? message)
        {
            return Config.Instance.QuietLinkDownloads && IsPlainLink(message);
        }

        public static bool IsPlainLink(Message? message)
        {
            return message == null
                || ((message.RequestHeaders == null || message.RequestHeaders.Count == 0)
                    && (message.ResponseHeaders == null || message.ResponseHeaders.Count == 0)
                    && string.IsNullOrEmpty(message.Cookies));
        }

        public static string QuietMarkerPath(string downloadId) => Path.Combine(Config.DataDir, downloadId + QuietMarkerExtension);

        /// <summary>
        /// Remembers that a download opens no windows, also after XDM restarts or when it leaves the queue.
        /// </summary>
        public static void MarkQuiet(string downloadId)
        {
            try
            {
                File.WriteAllText(QuietMarkerPath(downloadId), string.Empty);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not mark download as quiet");
            }
        }

        public static bool IsMarkedQuiet(string downloadId) => File.Exists(QuietMarkerPath(downloadId));

        public static void UnmarkQuiet(string downloadId)
        {
            try
            {
                File.Delete(QuietMarkerPath(downloadId));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not remove quiet marker");
            }
        }

        /// <summary>
        /// A checker for one batch of links, or null when skipping duplicates is turned off.
        /// </summary>
        public static DuplicateDownloadChecker? CreateDuplicateChecker()
        {
            if (!Config.Instance.SkipDuplicateDownloads)
            {
                return null;
            }

            var unfinished = new List<KeyValuePair<string?, string>>();
            try
            {
                if (AppDB.Instance.Downloads.LoadDownloads(out var inProgress, out _, QueryMode.InProgress))
                {
                    unfinished.AddRange(inProgress.Select(d => new KeyValuePair<string?, string>(d.TargetDir, d.Name)));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not load unfinished downloads for the duplicate check");
            }
            return new DuplicateDownloadChecker(unfinished, FileHelper.GetDownloadFolderByFileName);
        }

        /// <summary>
        /// Checks a download against the batch checker. A duplicate is written to the skipped list.
        /// </summary>
        /// <returns>True when the download should be skipped.</returns>
        public static bool SkipIfDuplicate(DuplicateDownloadChecker? checker, string fileName, string? folder, string? url,
            out DuplicateReason reason, out string destination)
        {
            reason = DuplicateReason.None;
            destination = fileName;
            if (checker == null)
            {
                return false;
            }

            reason = checker.Check(FileHelper.SanitizeFileName(fileName), folder, out var path);
            destination = path ?? fileName;
            if (reason == DuplicateReason.None)
            {
                return false;
            }

            try
            {
                SkippedDownloadList.Append(SkippedListPath, DateTime.Now, GetReasonText(reason), destination, url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not write the skipped downloads list");
            }
            return true;
        }

        public static string SkippedListPath => Path.Combine(Config.DataDir, SkippedDownloadList.FileName);

        public static string GetReasonText(DuplicateReason reason) => reason switch
        {
            DuplicateReason.FileExists => TextResource.GetText("SKIP_FILE_EXISTS"),
            DuplicateReason.AlreadyInList => TextResource.GetText("SKIP_IN_LIST"),
            DuplicateReason.RepeatedInBatch => TextResource.GetText("SKIP_REPEATED"),
            _ => string.Empty
        };
    }
}
