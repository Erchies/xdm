using System;
using System.Globalization;
using System.IO;

namespace XDM.Core.FileHosts
{
    /// <summary>
    /// Keeps the upload date of a file host download until XDM finishes it, then stamps it on
    /// the file as its modified date. The date lives in "&lt;id&gt;.filedate" next to the other
    /// per-download files, so it survives restarts and queued downloads without touching the
    /// download info format of the original XDM.
    /// </summary>
    public static class FileHostFileDates
    {
        public const string Extension = ".filedate";

        public static string PathFor(string dataDir, string downloadId) => Path.Combine(dataDir, downloadId + Extension);

        public static void Save(string dataDir, string downloadId, DateTime utc)
        {
            File.WriteAllText(PathFor(dataDir, downloadId),
                DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Applies and removes the saved date, if any. Best effort: a missing or unreadable date,
        /// or a file that cannot be touched, leaves the file as it is.
        /// </summary>
        /// <returns>True when the modified date was changed.</returns>
        public static bool Apply(string dataDir, string downloadId, string targetFile)
        {
            var path = PathFor(dataDir, downloadId);
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }
                var text = File.ReadAllText(path).Trim();
                File.Delete(path);

                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                    || !File.Exists(targetFile))
                {
                    return false;
                }
                File.SetLastWriteTimeUtc(targetFile, date.ToUniversalTime());
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is ArgumentException || ex is NotSupportedException)
            {
                return false;
            }
        }
    }
}
