using System;
using System.Collections.Generic;
using XDM.Core.FileHosts;

namespace XDM.Core.UI
{
    public interface IFileHostDownloadView
    {
        string LinksText { get; set; }

        /// <summary>
        /// Password used for links that do not carry their own.
        /// </summary>
        string Password { get; }

        string DownloadLocation { get; set; }

        bool CreateSubfolders { get; set; }

        string GofileAccountToken { get; set; }

        string GofileWebsiteTokenSalt { get; set; }

        string BunkrIncludeFilter { get; set; }

        string BunkrIgnoreFilter { get; set; }

        bool BunkrCleanName { get; set; }

        IEnumerable<ResolvedFile> SelectedFiles { get; }

        void SetFiles(IEnumerable<ResolvedFile> files);

        void AppendLog(FileHostLogLevel level, string message);

        void ClearLog();

        /// <summary>
        /// Disables the inputs while links are being resolved and enables the stop button.
        /// </summary>
        void SetBusy(bool busy);

        void SetStatus(string text);

        string? SelectFolder();

        void ShowWindow();

        void CloseWindow();

        event EventHandler BrowseClicked;

        event EventHandler ListClicked;

        event EventHandler StopClicked;

        event EventHandler DownloadClicked;

        event EventHandler<DownloadLaterEventArgs> DownloadLaterClicked;

        event EventHandler QueueSchedulerClicked;

        event EventHandler SelectionChanged;

        event EventHandler WindowClosed;
    }
}
