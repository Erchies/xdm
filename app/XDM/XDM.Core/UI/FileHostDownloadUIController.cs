using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TraceLog;
using Translations;
using XDM.Core.Downloader;
using XDM.Core.Downloader.Progressive.SingleHttp;
using XDM.Core.FileHosts;
using XDM.Core.Util;

namespace XDM.Core.UI
{
    /// <summary>
    /// Drives the GoFile / Bunkr dialog: resolves the pasted links in the background and hands
    /// every selected file to the regular XDM download engine.
    /// </summary>
    public class FileHostDownloadUIController
    {
        private readonly IFileHostDownloadView view;
        private FileHostContext? running;
        private List<ResolvedFile> resolvedFiles = new();
        private string? resolvedLinksText;
        private bool closed;

        public FileHostDownloadUIController(IFileHostDownloadView view, string? links = null)
        {
            this.view = view;

            view.DownloadLocation = Helpers.GetManualDownloadFolder() ?? Config.Instance.DefaultDownloadFolder;
            view.CreateSubfolders = Config.Instance.FileHostCreateSubfolders;
            view.GofileAccountToken = Config.Instance.GofileAccountToken ?? string.Empty;
            view.GofileWebsiteTokenSalt = string.IsNullOrEmpty(Config.Instance.GofileWebsiteTokenSalt)
                ? GofileResolver.DefaultWebsiteTokenSalt : Config.Instance.GofileWebsiteTokenSalt;
            view.BunkrIncludeFilter = Config.Instance.BunkrIncludeFilter ?? string.Empty;
            view.BunkrIgnoreFilter = Config.Instance.BunkrIgnoreFilter ?? string.Empty;
            view.BunkrCleanName = Config.Instance.BunkrCleanName;

            view.LinksText = string.IsNullOrEmpty(links) ? GetSupportedLinksFromClipboard() : links!;
            view.SetStatus(string.Empty);

            view.BrowseClicked += (_, _) =>
            {
                var folder = view.SelectFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    view.DownloadLocation = folder!;
                    Config.Instance.UserSelectedDownloadFolder = folder!;
                    Helpers.UpdateRecentFolderList(folder!);
                }
            };
            view.ListClicked += (_, _) => Resolve(null);
            view.StopClicked += (_, _) => running?.Cancel();
            view.DownloadClicked += (_, _) => Download(true, null);
            view.DownloadLaterClicked += (_, e) => Download(false, e.QueueId);
            view.QueueSchedulerClicked += (_, _) => ApplicationContext.Application.ShowQueueWindow(view);
            view.SelectionChanged += (_, _) => UpdateSummary();
            view.WindowClosed += (_, _) =>
            {
                closed = true;
                running?.Cancel();
            };
        }

        public void Run()
        {
            view.ShowWindow();
        }

        private void Download(bool startImmediately, string? queueId)
        {
            if (running != null)
            {
                return;
            }
            if (string.IsNullOrEmpty(view.DownloadLocation))
            {
                ApplicationContext.Application.ShowMessageBox(view, TextResource.GetText("MSG_CAT_FOLDER_MISSING"));
                return;
            }

            if (resolvedFiles.Count == 0 || resolvedLinksText != view.LinksText)
            {
                // Links changed since the last listing (or were never listed): list them and
                // download everything that was found.
                Resolve(() => AddDownloads(resolvedFiles, startImmediately, queueId));
                return;
            }

            AddDownloads(view.SelectedFiles.ToList(), startImmediately, queueId);
        }

        private void AddDownloads(IList<ResolvedFile> files, bool startImmediately, string? queueId)
        {
            if (files.Count == 0)
            {
                ApplicationContext.Application.ShowMessageBox(view, TextResource.GetText("BAT_SELECT_ITEMS"));
                return;
            }

            var location = view.DownloadLocation;
            var added = 0;
            foreach (var file in files)
            {
                string folder;
                try
                {
                    folder = FileHostNames.CombineWithin(location, file.RelativeFolder);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Rejected download folder");
                    view.AppendLog(FileHostLogLevel.Error, $"{file.Name}: {ex.Message}");
                    continue;
                }

                var info = new SingleSourceHTTPDownloadInfo
                {
                    Uri = file.Url,
                    File = file.Name,
                    Headers = file.Headers.ToDictionary(h => h.Key, h => new List<string>(h.Value)),
                    Cookies = file.Cookies
                };

                var id = ApplicationContext.CoreService.StartDownload(info, file.Name, FileNameFetchMode.None,
                    folder, startImmediately, null, Config.Instance.Proxy, queueId, false);
                if (id != null && file.LastModified is DateTime lastModified)
                {
                    try
                    {
                        FileHostFileDates.Save(Config.DataDir, id, lastModified);
                    }
                    catch (Exception ex)
                    {
                        // Only the file date is lost; the download itself is already queued.
                        Log.Debug(ex, "Could not save the upload date of " + file.Name);
                    }
                }
                added++;
            }

            // Keep the window (and its error log) open when nothing could be added.
            if (added > 0)
            {
                view.CloseWindow();
            }
        }

        private void Resolve(Action? onResolved)
        {
            if (running != null)
            {
                return;
            }

            var linksText = view.LinksText;
            var links = FileHostLinkParser.Parse(linksText, view.Password);
            if (links.Count == 0)
            {
                ApplicationContext.Application.ShowMessageBox(view, TextResource.GetText("FH_NO_LINKS"));
                return;
            }

            SaveOptions();
            var options = BuildOptions();
            var context = new FileHostContext((level, message) =>
                ApplicationContext.Application.RunOnUiThread(() =>
                {
                    if (!closed)
                    {
                        view.AppendLog(level, message);
                    }
                }));

            running = context;
            resolvedFiles = new List<ResolvedFile>();
            resolvedLinksText = null;
            view.ClearLog();
            view.SetFiles(Enumerable.Empty<ResolvedFile>());
            view.SetBusy(true);

            new Thread(() =>
            {
                var files = new List<ResolvedFile>();
                var http = new FileHostWebClient(Config.Instance.Proxy, Config.Instance.NetworkTimeout);
                var resolvers = FileHostResolvers.Create(http, options);

                for (var i = 0; i < links.Count && !context.IsCancelled; i++)
                {
                    var link = links[i];
                    var index = i;
                    ApplicationContext.Application.RunOnUiThread(() =>
                    {
                        if (!closed)
                        {
                            view.SetStatus(string.Format(TextResource.GetText("FH_STATUS_RESOLVING"), index + 1, links.Count, link.Url));
                        }
                    });

                    var resolver = FileHostResolvers.Find(resolvers, link.Url);
                    if (resolver == null)
                    {
                        context.Error(string.Format(TextResource.GetText("FH_INVALID_URL"), link.Url));
                        continue;
                    }

                    try
                    {
                        var found = resolver.Resolve(link, context);
                        context.Info(string.Format(TextResource.GetText("FH_LINK_DONE"), link.Url, found.Count));
                        files.AddRange(found);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (FileHostException ex)
                    {
                        context.Error($"{link.Url}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error resolving " + link.Url);
                        context.Error($"{link.Url}: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                ApplicationContext.Application.RunOnUiThread(() => OnResolved(context, linksText, files, onResolved));
            })
            { IsBackground = true, Name = "filehost-resolver" }.Start();
        }

        private void OnResolved(FileHostContext context, string linksText, List<ResolvedFile> files, Action? onResolved)
        {
            running = null;
            if (closed)
            {
                return;
            }

            view.SetBusy(false);
            if (context.IsCancelled)
            {
                view.SetStatus(TextResource.GetText("FH_STATUS_CANCELLED"));
                return;
            }

            resolvedFiles = files;
            resolvedLinksText = linksText;
            view.SetFiles(files);
            UpdateSummary();

            if (files.Count == 0)
            {
                ApplicationContext.Application.ShowMessageBox(view, TextResource.GetText("FH_NOTHING_FOUND"));
                return;
            }
            onResolved?.Invoke();
        }

        private void UpdateSummary()
        {
            if (running != null)
            {
                return;
            }
            var selected = view.SelectedFiles.ToList();
            var knownSize = selected.Where(f => f.Size > 0).Sum(f => f.Size);
            view.SetStatus(string.Format(TextResource.GetText("FH_STATUS_FOUND"), resolvedFiles.Count, selected.Count,
                knownSize > 0 ? FormattingHelper.FormatSize(knownSize) : "---"));
        }

        private static string GetSupportedLinksFromClipboard()
        {
            try
            {
                var links = FileHostLinkParser.Parse(ApplicationContext.Application.GetUrlFromClipboard())
                    .Where(link => FileHostResolvers.IsSupported(link.Url))
                    .Select(link => link.Url);
                return string.Join(Environment.NewLine, links);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not read the clipboard");
                return string.Empty;
            }
        }

        private void SaveOptions()
        {
            Config.Instance.FileHostCreateSubfolders = view.CreateSubfolders;
            Config.Instance.GofileAccountToken = view.GofileAccountToken.Trim();
            var salt = view.GofileWebsiteTokenSalt.Trim();
            Config.Instance.GofileWebsiteTokenSalt = salt == GofileResolver.DefaultWebsiteTokenSalt ? string.Empty : salt;
            Config.Instance.BunkrIncludeFilter = view.BunkrIncludeFilter.Trim();
            Config.Instance.BunkrIgnoreFilter = view.BunkrIgnoreFilter.Trim();
            Config.Instance.BunkrCleanName = view.BunkrCleanName;
            Config.SaveConfig();
        }

        private static FileHostOptions BuildOptions()
        {
            return new FileHostOptions
            {
                GofileAccountToken = Config.Instance.GofileAccountToken ?? string.Empty,
                GofileWebsiteTokenSalt = Config.Instance.GofileWebsiteTokenSalt ?? string.Empty,
                BunkrInclude = FileHostResolvers.ParseFilter(Config.Instance.BunkrIncludeFilter),
                BunkrIgnore = FileHostResolvers.ParseFilter(Config.Instance.BunkrIgnoreFilter),
                BunkrCleanName = Config.Instance.BunkrCleanName,
                CreateSubfolders = Config.Instance.FileHostCreateSubfolders,
                Retries = Math.Max(1, Math.Min(Config.Instance.MaxRetry, 5))
            };
        }
    }
}
