using System;
using System.Collections.Generic;
using System.Threading;

namespace XDM.Core.FileHosts
{
    public enum FileHostLogLevel
    {
        Info, Warning, Error
    }

    /// <summary>
    /// A file discovered on a file host page, ready to be handed to the XDM download engine.
    /// </summary>
    public class ResolvedFile
    {
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// Link the user pasted (album, folder or file page).
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Folder relative to the download location. Built only from sanitized names,
        /// so it never contains ".." or rooted components.
        /// </summary>
        public string RelativeFolder { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Size reported by the host, or -1 when unknown.
        /// </summary>
        public long Size { get; set; } = -1;

        public Dictionary<string, List<string>> Headers { get; set; } = new();

        public string? Cookies { get; set; }

        /// <summary>
        /// Upload date reported by the host (UTC). Becomes the modified date of the downloaded
        /// file; null keeps the download time.
        /// </summary>
        public DateTime? LastModified { get; set; }
    }

    public class FileHostLink
    {
        public FileHostLink(string url, string? password)
        {
            Url = url;
            Password = password;
        }

        public string Url { get; }

        public string? Password { get; }
    }

    public class FileHostOptions
    {
        public string GofileAccountToken { get; set; } = string.Empty;

        public string GofileWebsiteTokenSalt { get; set; } = string.Empty;

        public string GofileUserAgent { get; set; } = string.Empty;

        /// <summary>
        /// Bunkr: when not empty, only names containing at least one of these words are kept.
        /// </summary>
        public IList<string> BunkrInclude { get; set; } = new List<string>();

        /// <summary>
        /// Bunkr: names containing any of these words are skipped.
        /// </summary>
        public IList<string> BunkrIgnore { get; set; } = new List<string>();

        /// <summary>
        /// Bunkr: name files after the page title only, without the CDN code ("--clean-name").
        /// </summary>
        public bool BunkrCleanName { get; set; }

        /// <summary>
        /// Put the files of each GoFile folder / Bunkr album in its own subfolder.
        /// </summary>
        public bool CreateSubfolders { get; set; } = true;

        public int Retries { get; set; } = 5;

        public int MaxParallelRequests { get; set; } = 4;

        /// <summary>
        /// First retry waits this long, doubling on each attempt (capped at 15s).
        /// </summary>
        public int RetryBaseDelayMilliseconds { get; set; } = 1000;
    }

    public class FileHostException : Exception
    {
        public FileHostException(string message) : base(message) { }

        public FileHostException(string message, Exception inner) : base(message, inner) { }
    }

    public class FileHostPasswordException : FileHostException
    {
        public FileHostPasswordException(string message) : base(message) { }
    }

    /// <summary>
    /// Connection level failure (no HTTP response at all). Always worth a retry.
    /// </summary>
    public class FileHostNetworkException : FileHostException
    {
        public FileHostNetworkException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Carries logging and cooperative cancellation through a resolve operation.
    /// </summary>
    public class FileHostContext
    {
        private readonly ManualResetEvent cancelled = new(false);
        private readonly Action<FileHostLogLevel, string>? log;

        public FileHostContext(Action<FileHostLogLevel, string>? log = null)
        {
            this.log = log;
        }

        public bool IsCancelled => cancelled.WaitOne(0);

        public void Cancel() => cancelled.Set();

        public void ThrowIfCancelled()
        {
            if (IsCancelled)
            {
                throw new OperationCanceledException();
            }
        }

        /// <summary>
        /// Waits for the given time. Returns false if cancelled meanwhile.
        /// </summary>
        public bool Delay(int milliseconds)
        {
            return milliseconds <= 0 ? !IsCancelled : !cancelled.WaitOne(milliseconds);
        }

        public void Info(string message) => log?.Invoke(FileHostLogLevel.Info, message);

        public void Warn(string message) => log?.Invoke(FileHostLogLevel.Warning, message);

        public void Error(string message) => log?.Invoke(FileHostLogLevel.Error, message);
    }

    public interface IFileHostResolver
    {
        string Name { get; }

        bool CanHandle(string url);

        /// <summary>
        /// Lists every downloadable file behind the link.
        /// </summary>
        /// <exception cref="FileHostException">The host refused or returned something unusable.</exception>
        /// <exception cref="OperationCanceledException">The context was cancelled.</exception>
        IList<ResolvedFile> Resolve(FileHostLink link, FileHostContext context);
    }
}
