using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TraceLog;
using Translations;
using XDM.Core.UI;
using XDM.Core;
using XDM.Core.Downloader;
using XDM.Core.Util;
using XDM.Wpf.UI.Dialogs.About;
using XDM.Wpf.UI.Dialogs.BatchDownload;
using XDM.Wpf.UI.Dialogs.CompletedDialog;
using XDM.Wpf.UI.Dialogs.CredentialDialog;
using XDM.Wpf.UI.Dialogs.DeleteConfirm;
using XDM.Wpf.UI.Dialogs.DownloadSelection;
using XDM.Wpf.UI.Dialogs.LanguageSettings;
using XDM.Wpf.UI.Dialogs.NewDownload;
using XDM.Wpf.UI.Dialogs.NewVideoDownload;
using XDM.Wpf.UI.Dialogs.ProgressWindow;
using XDM.Wpf.UI.Dialogs.PropertiesDialog;
using XDM.Wpf.UI.Dialogs.QueuesWindow;
using XDM.Wpf.UI.Dialogs.RefreshLink;
using XDM.Wpf.UI.Dialogs.Settings;
using XDM.Wpf.UI.Dialogs.Updater;
using XDM.Wpf.UI.Dialogs.VideoDownloader;
using XDM.Wpf.UI.Dialogs.Widget;
using XDM.Wpf.UI.Win32;
using XDM.Wpf.UI.Dialogs.MediaCapture;

namespace XDM.Wpf.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IApplicationWindow
    {
        private ObservableCollection<InProgressDownloadEntryWrapper> inProgressList
            = new ObservableCollection<InProgressDownloadEntryWrapper>();
        private ObservableCollection<FinishedDownloadEntryWrapper> finishedList
            = new ObservableCollection<FinishedDownloadEntryWrapper>();
        private ObservableCollection<AllDownloadsRow> allList = new ObservableCollection<AllDownloadsRow>();

        // Positions in the category list: "All" first, then the original XDM entries.
        private const int AllIndex = 0;
        private const int InProgressIndex = 1;
        private const int FinishedIndex = 2;
        private const int FirstCategoryIndex = 3;

        private IButton newButton, deleteButton, pauseButton, resumeButton, openFileButton, openFolderButton;
        private GridViewColumnHeader? finishedListViewSortCol = null;
        private SortAdorner? finishedListViewSortAdorner = null;
        private GridViewColumnHeader? inProgressListViewSortCol = null;
        private SortAdorner? inProgressListViewSortAdorner = null;
        private GridViewColumnHeader? allListViewSortCol = null;
        private SortAdorner? allListViewSortAdorner = null;
        private DispatcherTimer statsTimer;
        private int statsTicks;
        private string freeSpaceText = string.Empty;
        private MessageLoop messageLoop;
        private Win32ClipboarMonitor clipboarMonitor;

        private IMenuItem[] menuItems;

        public MainWindow()
        {
            InitializeComponent();

            newButton = new ButtonWrapper(this.BtnNew);
            deleteButton = new ButtonWrapper(this.BtnDelete);
            pauseButton = new ButtonWrapper(this.BtnPause);
            resumeButton = new ButtonWrapper(this.BtnResume);
            openFileButton = new ButtonWrapper(this.BtnOpen);
            openFolderButton = new ButtonWrapper(this.BtnOpenFolder);
            var categories = new List<CategoryWrapper>();
            categories.Add(new CategoryWrapper() { IsTopLevel = true, DisplayName = TextResource.GetText("ALL_DOWNLOADS"), VectorIcon = "ri-list-settings-fill" });
            categories.Add(new CategoryWrapper() { IsTopLevel = true, DisplayName = TextResource.GetText("ALL_UNFINISHED"), VectorIcon = "ri-arrow-down-line" });
            categories.Add(new CategoryWrapper() { IsTopLevel = true, DisplayName = TextResource.GetText("ALL_FINISHED"), VectorIcon = "ri-check-line" });
            categories.AddRange(Config.Instance.Categories.Select(c => new CategoryWrapper(c)
            {
                VectorIcon = IconMap.GetVectorNameForCategory(c.Name)
            }));
            lvCategory.ItemsSource = categories;

            lvInProgress.ItemsSource = inProgressList;
            lvFinished.ItemsSource = finishedList;
            lvAll.ItemsSource = allList;

            lvInProgress.SelectionChanged += (sender, args) =>
            {
                this.SelectionChanged?.Invoke(sender, args);
            };

            lvFinished.SelectionChanged += (sender, args) =>
            {
                this.SelectionChanged?.Invoke(sender, args);
            };

            lvAll.SelectionChanged += (sender, args) =>
            {
                this.SelectionChanged?.Invoke(sender, args);
            };

            lvInProgress.IsVisibleChanged += (_, _) =>
            {
                if (lvInProgress.Visibility == Visibility.Visible)
                {
                    InProgressListViewInitialSortIfNotAlreadySorted();
                }
            };

            lvCategory.SelectedIndex = AllIndex;
            this.Loaded += MainWindow_Loaded;
            CreateMenuItems();

            statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statsTimer.Tick += (_, _) => UpdateStatusBarStats();
            statsTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            FinishedListViewInitialSortIfNotAlreadySorted();
            AllListViewInitialSortIfNotAlreadySorted();
            UpdateBrowserMonitorButton();
            UpdateStatusBarStats();
        }

        private void AllListViewInitialSortIfNotAlreadySorted()
        {
            //sort the "All" list view by date
            if (allListViewSortCol == null)
            {
                var col = (GridViewColumnHeader)FindName("lvAll_DateAdded");
                var layer = AdornerLayer.GetAdornerLayer(col);
                if (layer != null)
                {
                    allListViewSortCol = col;
                    allListViewSortAdorner = new SortAdorner(allListViewSortCol, ListSortDirection.Descending);
                    layer.Add(allListViewSortAdorner);
                }
                lvAll.Items.SortDescriptions.Clear();
                lvAll.Items.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
            }
        }

        private void InProgressListViewInitialSortIfNotAlreadySorted()
        {
            //sort in-progress list view by date
            if (inProgressListViewSortCol == null)
            {
                var col = (GridViewColumnHeader)FindName("lvInProgress_DateAdded");
                var layer = AdornerLayer.GetAdornerLayer(col);
                if (layer != null)
                {
                    inProgressListViewSortCol = col;
                    inProgressListViewSortAdorner = new SortAdorner(inProgressListViewSortCol, ListSortDirection.Descending);
                    layer.Add(inProgressListViewSortAdorner);
                }
                lvInProgress.Items.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
            }
        }

        private void FinishedListViewInitialSortIfNotAlreadySorted()
        {
            //sort finished list view by date
            if (finishedListViewSortCol == null)
            {
                var col = (GridViewColumnHeader)FindName("lvFinished_DateAdded");
                var layer = AdornerLayer.GetAdornerLayer(col);
                if (layer != null)
                {
                    finishedListViewSortCol = col;
                    finishedListViewSortAdorner = new SortAdorner(finishedListViewSortCol, ListSortDirection.Descending);
                    layer.Add(finishedListViewSortAdorner);
                }
                //finishedListViewSortCol = (GridViewColumnHeader)FindName("lvFinished_DateAdded");
                //finishedListViewSortAdorner = new SortAdorner(finishedListViewSortCol, ListSortDirection.Descending);
                //AdornerLayer.GetAdornerLayer(finishedListViewSortCol).Add(finishedListViewSortAdorner);
                lvFinished.Items.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
            }
        }

        private void lvCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TxtSearch.Text = string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var index = lvCategory.SelectedIndex;
            if (index == AllIndex)
            {
                lvAll.Visibility = Visibility.Visible;
                lvInProgress.Visibility = Visibility.Collapsed;
                lvFinished.Visibility = Visibility.Collapsed;
                AllListViewInitialSortIfNotAlreadySorted();

                var view = (ListCollectionView)CollectionViewSource.GetDefaultView(lvAll.ItemsSource);
                view.Filter = a => Helpers.IsOfCategoryOrMatchesKeyword(((AllDownloadsRow)a).Name, TxtSearch.Text, null);
                CategoryChanged?.Invoke(this, new CategoryChangedEventArgs { Level = 0, Index = 0 });
            }
            else if (index == InProgressIndex)
            {
                lvAll.Visibility = Visibility.Collapsed;
                lvInProgress.Visibility = Visibility.Visible;
                lvFinished.Visibility = Visibility.Collapsed;
                InProgressListViewInitialSortIfNotAlreadySorted();

                CategoryChanged?.Invoke(this, new CategoryChangedEventArgs { Level = 0, Index = 0 });
            }
            else if (index >= FinishedIndex)
            {
                lvAll.Visibility = Visibility.Collapsed;
                lvInProgress.Visibility = Visibility.Collapsed;
                lvFinished.Visibility = Visibility.Visible;

                ListCollectionView view = (ListCollectionView)
                        CollectionViewSource.GetDefaultView(lvFinished.ItemsSource);
                if (index >= FirstCategoryIndex)
                {
                    CategoryWrapper? cat = (CategoryWrapper)lvCategory.SelectedItem;
                    view.Filter = a => IsCategoryMatched((FinishedDownloadEntryWrapper)a, cat);
                    CategoryChanged?.Invoke(this, new CategoryChangedEventArgs
                    {
                        Level = 1,
                        Index = index - FirstCategoryIndex,
                        Category = cat.category
                    });
                }
                else
                {
                    view.Filter = a => IsCategoryMatched((FinishedDownloadEntryWrapper)a, null);
                    CategoryChanged?.Invoke(this, new CategoryChangedEventArgs { Level = 0, Index = 1 });
                }
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool IsCategoryMatched(FinishedDownloadEntryWrapper entry, CategoryWrapper? category)
        {
            return Helpers.IsOfCategoryOrMatchesKeyword(entry.Name, TxtSearch.Text, category?.category);
        }

        public event EventHandler<CategoryChangedEventArgs> CategoryChanged;
        public event EventHandler? InProgressContextMenuOpening;
        public event EventHandler? FinishedContextMenuOpening;
        public event EventHandler? SelectionChanged;
        public event EventHandler? NewDownloadClicked;
        public event EventHandler? YoutubeDLDownloadClicked;
        public event EventHandler? BatchDownloadClicked;
        public event EventHandler? SettingsClicked;
        public event EventHandler? ClearAllFinishedClicked;
        public event EventHandler? ExportClicked;
        public event EventHandler? ImportClicked;
        public event EventHandler? BrowserMonitoringButtonClicked;
        public event EventHandler? BrowserMonitoringSettingsClicked;
        public event EventHandler? UpdateClicked;
        public event EventHandler? HelpClicked;
        public event EventHandler? SupportPageClicked;
        public event EventHandler? BugReportClicked;
        public event EventHandler? CheckForUpdateClicked;
        public event EventHandler? SchedulerClicked;
        public event EventHandler? DownloadListDoubleClicked;
        public event EventHandler? ClipboardChanged;
        public event EventHandler? WindowCreated;

        public IEnumerable<FinishedDownloadItem> FinishedDownloads
        {
            get => this.finishedList.Select(x => x.DownloadEntry);
            set
            {
                this.finishedList = new ObservableCollection<FinishedDownloadEntryWrapper>(
                    value.Select(x => new FinishedDownloadEntryWrapper(x)));
                this.lvFinished.ItemsSource = finishedList;
                FinishedListViewInitialSortIfNotAlreadySorted();
                RebuildAllList();
            }
        }

        public IEnumerable<InProgressDownloadItem> InProgressDownloads
        {
            get => this.inProgressList.Select(x => x.DownloadEntry);
            set
            {
                this.inProgressList = new ObservableCollection<InProgressDownloadEntryWrapper>(
                    value.Select(x => new InProgressDownloadEntryWrapper(x)));
                this.lvInProgress.ItemsSource = inProgressList;
                InProgressListViewInitialSortIfNotAlreadySorted();
                RebuildAllList();
            }
        }

        // In the "All" list the selection is split by kind, so the existing actions (pause,
        // open, delete...) keep working on the part they understand.
        public IList<IInProgressDownloadRow> SelectedInProgressRows =>
            lvCategory.SelectedIndex == AllIndex
            ? this.lvAll.SelectedItems.OfType<AllDownloadsRow>().Select(r => r.InProgress).OfType<IInProgressDownloadRow>().ToList()
            : this.lvInProgress.SelectedItems.OfType<IInProgressDownloadRow>().ToList();

        public IList<IFinishedDownloadRow> SelectedFinishedRows =>
            lvCategory.SelectedIndex == AllIndex
            ? this.lvAll.SelectedItems.OfType<AllDownloadsRow>().Select(r => r.Finished).OfType<IFinishedDownloadRow>().ToList()
            : this.lvFinished.SelectedItems.OfType<IFinishedDownloadRow>().ToList();

        public IButton NewButton => newButton;

        public IButton DeleteButton => deleteButton;

        public IButton PauseButton => pauseButton;

        public IButton ResumeButton => resumeButton;

        public IButton OpenFileButton => openFileButton;

        public IButton OpenFolderButton => openFolderButton;

        /// <summary>
        /// In the "All" list this follows the selection: with finished rows selected, the finished actions apply.
        /// </summary>
        public bool IsInProgressViewSelected => lvCategory.SelectedIndex == InProgressIndex
            || (lvCategory.SelectedIndex == AllIndex && !lvAll.SelectedItems.OfType<AllDownloadsRow>().Any(r => r.IsFinished));

        public IMenuItem[] MenuItems => this.menuItems;

        public Dictionary<string, IMenuItem> MenuItemMap { get; private set; }

        public IInProgressDownloadRow FindInProgressItem(string id) =>
            this.lvInProgress.Items.OfType<IInProgressDownloadRow>()
            .Where(x => x.DownloadEntry.Id == id).FirstOrDefault();

        public IFinishedDownloadRow FindFinishedItem(string id) =>
            this.lvFinished.Items.OfType<IFinishedDownloadRow>()
            .Where(x => x.DownloadEntry.Id == id).FirstOrDefault();

        public void AddToTop(InProgressDownloadItem entry)
        {
            var row = new InProgressDownloadEntryWrapper(entry);
            this.inProgressList.Add(row);
            this.allList.Add(new AllDownloadsRow(row));
        }

        public void AddToTop(FinishedDownloadItem entry)
        {
            var row = new FinishedDownloadEntryWrapper(entry);
            this.finishedList.Add(row);
            this.allList.Add(new AllDownloadsRow(row));
        }

        /// <summary>
        /// XDM jumps between its lists as downloads start and finish; the "All" list already shows both.
        /// </summary>
        public void SwitchToInProgressView()
        {
            if (lvCategory.SelectedIndex != AllIndex)
            {
                lvCategory.SelectedIndex = InProgressIndex;
            }
        }

        public void ClearInProgressViewSelection()
        {
            lvInProgress.UnselectAll();
        }

        public void SwitchToFinishedView()
        {
            if (lvCategory.SelectedIndex != AllIndex)
            {
                lvCategory.SelectedIndex = FinishedIndex;
            }
        }

        public void ClearFinishedViewSelection()
        {
            lvFinished.UnselectAll();
        }

        public bool Confirm(object? window, string text)
        {
            return MessageBox.Show((Window)(window ?? this), text, "XDM", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        public void ConfirmDelete(string text, out bool approved, out bool deleteFiles)
        {
            DeleteConfirmDialog dc = new() { DescriptionText = text, Owner = this };
            approved = false;
            deleteFiles = false;
            bool? ret = dc.ShowDialog(this);
            if (ret.HasValue && ret.Value)
            {
                approved = true;
                deleteFiles = dc.ShouldDeleteFile;
            }
        }

        public void RunOnUIThread(Action action)
        {
            Dispatcher.BeginInvoke(action);
        }

        public void RunOnUIThread(Action<string, int, double, long> action, string id, int progress, double speed, long eta)
        {
            Dispatcher.BeginInvoke(action, id, progress, speed, eta);
        }

        public void Delete(IInProgressDownloadRow row)
        {
            this.inProgressList.Remove((InProgressDownloadEntryWrapper)row);
            RemoveFromAllList(row);
        }

        public void Delete(IFinishedDownloadRow row)
        {
            this.finishedList.Remove((FinishedDownloadEntryWrapper)row);
            RemoveFromAllList(row);
        }

        public void DeleteAllFinishedDownloads()
        {
            if (MessageBox.Show(this, TextResource.GetText("MENU_DELETE_COMPLETED"), "XDM", MessageBoxButton.YesNo)
                != MessageBoxResult.Yes)
            {
                return;
            }
            finishedList.Clear();
            foreach (var row in allList.Where(r => r.IsFinished).ToList())
            {
                row.Detach();
                allList.Remove(row);
            }
        }

        public void Delete(IEnumerable<IInProgressDownloadRow> rows)
        {
            foreach (var row in rows)
            {
                inProgressList.Remove((InProgressDownloadEntryWrapper)row);
                RemoveFromAllList(row);
            }
        }

        public void Delete(IEnumerable<IFinishedDownloadRow> rows)
        {
            foreach (var row in rows)
            {
                finishedList.Remove((FinishedDownloadEntryWrapper)row);
                RemoveFromAllList(row);
            }
        }

        public string? GetUrlFromClipboard()
        {
            return Clipboard.GetText();
        }

        public void ShowUpdateAvailableNotification()
        {
            RunOnUIThread(() =>
            {
                PathHelp.Data = (Geometry)FindResource("ri-notification-3-fill");
                PathHelp.Fill = (SolidColorBrush)FindResource("color-update-avaliable");
                TxtHelp.Text = TextResource.GetText("MSG_UPDATE_AVAILABLE");
                BtnHelp.Tag = new object();
            });
        }

        public void ClearUpdateInformation()
        {
            RunOnUIThread(() =>
            {
                PathHelp.Data = (Geometry)FindResource("ri-question-line");
                TxtHelp.Text = TextResource.GetText("LBL_SUPPORT_PAGE");
                PathHelp.Fill = (SolidColorBrush)FindResource("StatusbarIconcolor");
                BtnHelp.Tag = null;
            });
        }

        public void OpenNewDownloadMenu()
        {
            var nctx = (ContextMenu)FindResource("newDownloadContextMenu");
            nctx.PlacementTarget = BtnNew;
            nctx.Placement = PlacementMode.Bottom;
            nctx.IsOpen = true;
        }

        public void SetClipboardText(string text)
        {
            Clipboard.SetText(text);
        }

        public void SetClipboardFile(string file)
        {
            var sc = new StringCollection();
            sc.Add(file);
            Clipboard.SetFileDropList(sc);
        }

        public void UpdateBrowserMonitorButton()
        {
            this.MonitoringToggleIcon.Data = (Geometry)FindResource(Config.Instance.IsBrowserMonitoringEnabled ?
                "ri-toggle-fill" : "ri-toggle-line");
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);

#if NET45_OR_GREATER
            if (App.Skin == Skin.Dark)
            {
                helper.EnsureHandle();
                DarkModeHelper.UseImmersiveDarkMode(helper.Handle, true);
            }
#endif
            clipboarMonitor = new Win32ClipboarMonitor(helper.Handle);
            clipboarMonitor.ClipboardChanged += (sender, args) => this.ClipboardChanged?.Invoke(this, EventArgs.Empty);
            this.messageLoop = new MessageLoop(clipboarMonitor);
            messageLoop.Start(helper.Handle);
            WindowCreated?.Invoke(this, EventArgs.Empty);
        }


        private void lvFinished_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader column)
            {
                string sortBy = (string)column.Tag;
                if (string.IsNullOrEmpty(sortBy))
                {
                    return;
                }
                if (finishedListViewSortCol != null)
                {
                    AdornerLayer.GetAdornerLayer(finishedListViewSortCol).Remove(finishedListViewSortAdorner);
                    lvFinished.Items.SortDescriptions.Clear();
                }

                ListSortDirection newDir = ListSortDirection.Ascending;
                if (finishedListViewSortCol == column && finishedListViewSortAdorner?.Direction == newDir)
                    newDir = ListSortDirection.Descending;

                finishedListViewSortCol = column;
                finishedListViewSortAdorner = new SortAdorner(finishedListViewSortCol, newDir);
                AdornerLayer.GetAdornerLayer(finishedListViewSortCol).Add(finishedListViewSortAdorner);
                lvFinished.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
            }
        }

        private void lvAll_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader column)
            {
                string sortBy = (string)column.Tag;
                if (string.IsNullOrEmpty(sortBy))
                {
                    return;
                }
                if (allListViewSortCol != null)
                {
                    AdornerLayer.GetAdornerLayer(allListViewSortCol).Remove(allListViewSortAdorner);
                }
                lvAll.Items.SortDescriptions.Clear();

                ListSortDirection newDir = ListSortDirection.Ascending;
                if (allListViewSortCol == column && allListViewSortAdorner?.Direction == newDir)
                    newDir = ListSortDirection.Descending;

                allListViewSortCol = column;
                allListViewSortAdorner = new SortAdorner(allListViewSortCol, newDir);
                AdornerLayer.GetAdornerLayer(allListViewSortCol).Add(allListViewSortAdorner);
                lvAll.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
            }
        }

        /// <summary>
        /// Rebuilds the "All" list after XDM replaces one of the two lists (startup, import),
        /// keeping the sort order the user picked.
        /// </summary>
        private void RebuildAllList()
        {
            var sort = lvAll.Items.SortDescriptions.ToList();
            foreach (var row in allList)
            {
                row.Detach();
            }
            allList = new ObservableCollection<AllDownloadsRow>(
                inProgressList.Select(r => new AllDownloadsRow(r))
                .Concat(finishedList.Select(r => new AllDownloadsRow(r))));
            lvAll.ItemsSource = allList;
            lvAll.Items.SortDescriptions.Clear();
            foreach (var description in sort)
            {
                lvAll.Items.SortDescriptions.Add(description);
            }
            if (lvCategory.SelectedIndex == AllIndex)
            {
                ApplyFilter();
            }
        }

        private void RemoveFromAllList(object row)
        {
            var allRow = allList.FirstOrDefault(r => ReferenceEquals(r.Source, row));
            if (allRow != null)
            {
                allRow.Detach();
                allList.Remove(allRow);
            }
        }

        private void UpdateStatusBarStats()
        {
            if (!IsVisible)
            {
                return;
            }

            var stats = DownloadStats.GetSnapshot();
            int downloading = 0, waiting = 0, stopped = 0;
            foreach (var row in inProgressList)
            {
                switch (row.Status)
                {
                    case DownloadStatus.Downloading: downloading++; break;
                    case DownloadStatus.Waiting: waiting++; break;
                    default: stopped++; break;
                }
            }

            // Free space needs a disk query and changes slowly: refresh it every 10 seconds.
            if (statsTicks++ % 10 == 0)
            {
                freeSpaceText = GetFreeSpaceText();
            }

            var parts = new List<string>
            {
                "\u2193 " + (stats.TotalSpeed > 0 ? FormattingHelper.FormatSize(stats.TotalSpeed) : "0B") + "/s",
                string.Format(TextResource.GetText("SB_DOWNLOADING"), downloading)
            };
            if (waiting > 0)
            {
                parts.Add(string.Format(TextResource.GetText("SB_WAITING"), waiting));
            }
            if (stopped > 0)
            {
                parts.Add(string.Format(TextResource.GetText("SB_STOPPED"), stopped));
            }
            if (stats.Remaining > 0)
            {
                parts.Add(string.Format(TextResource.GetText("SB_REMAINING"), FormattingHelper.FormatSize(stats.Remaining),
                    stats.EtaSeconds >= 0 ? FormattingHelper.ToHMS(stats.EtaSeconds) : "---"));
            }
            if (stats.SessionBytes > 0)
            {
                parts.Add(string.Format(TextResource.GetText("SB_SESSION"), FormattingHelper.FormatSize(stats.SessionBytes)));
                parts.Add(string.Format(TextResource.GetText("SB_PEAK"), FormattingHelper.FormatSize(stats.PeakSpeed) + "/s"));
            }
            parts.Add(string.Format(TextResource.GetText("SB_FINISHED"), finishedList.Count,
                FormattingHelper.FormatSize(finishedList.Where(r => r.Size > 0).Sum(r => r.Size))));
            if (freeSpaceText.Length > 0)
            {
                parts.Add(freeSpaceText);
            }

            TxtStats.Text = string.Join("  \u2022  ", parts);
            TxtStats.ToolTip = string.Join(Environment.NewLine, parts);
        }

        private static string GetFreeSpaceText()
        {
            try
            {
                var root = Path.GetPathRoot(Config.Instance.DefaultDownloadFolder);
                if (string.IsNullOrEmpty(root))
                {
                    return string.Empty;
                }
                var drive = new DriveInfo(root);
                return drive.IsReady
                    ? string.Format(TextResource.GetText("SB_FREE"), FormattingHelper.FormatSize(drive.AvailableFreeSpace), drive.Name.TrimEnd('\\'))
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not read free disk space");
                return string.Empty;
            }
        }

        private void menuSkippedDownloads_Click(object sender, RoutedEventArgs e)
        {
            var file = LinkDownloads.SkippedListPath;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, TextResource.GetText("MSG_NO_SKIPPED_DOWNLOADS"), "XDM");
                return;
            }
            if (!PlatformHelper.OpenFile(file))
            {
                MessageBox.Show(this, TextResource.GetText("ERR_MSG_FILE_NOT_FOUND_MSG"), "XDM");
            }
        }

        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            var nctx = (ContextMenu)FindResource("ctxMainMenu");
            if (nctx.Placement != PlacementMode.Bottom || nctx.PlacementTarget != BtnMenu)
            {
                nctx.Placement = PlacementMode.Bottom;
                nctx.PlacementTarget = BtnMenu;
            }
            nctx.IsOpen = true;
        }

        private void ctxMainMenu_LayoutUpdated(object sender, EventArgs e)
        {
            var ctx = (ContextMenu)FindResource("ctxMainMenu");
            if (ctx.HorizontalOffset != 0) return;
            ctx.HorizontalOffset = BtnMenu.ActualWidth - ctx.ActualWidth;
        }

        private void menuExit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void menuLanguage_Click(object sender, RoutedEventArgs e)
        {
            var langDlg = new LanguageSettingsWindow
            {
                Owner = this
            };
            langDlg.ShowDialog(this);
        }

        private void BtnQueue_Click(object sender, RoutedEventArgs e)
        {
            this.SchedulerClicked?.Invoke(sender, e);
        }

        private void menuSettings_Click(object sender, RoutedEventArgs e)
        {
            this.SettingsClicked?.Invoke(this, e);
        }

        private void BtnMonitoring_Click(object sender, RoutedEventArgs e)
        {
            BrowserMonitoringButtonClicked?.Invoke(sender, e);
        }

        private void menuClearFinished_Click(object sender, RoutedEventArgs e)
        {
            this.ClearAllFinishedClicked?.Invoke(sender, e);
        }

        private void menuBrowserMonitor_Click(object sender, RoutedEventArgs e)
        {
            BrowserMonitoringSettingsClicked?.Invoke(sender, e);
        }

        private void menuImport_Click(object sender, RoutedEventArgs e)
        {
            ImportClicked?.Invoke(sender, e);
        }

        private void menuExport_Click(object sender, RoutedEventArgs e)
        {
            ExportClicked?.Invoke(sender, e);
        }

        private void menuHelpAndSupport_Click(object sender, RoutedEventArgs e)
        {
            SupportPageClicked?.Invoke(sender, e);
        }

        private void menuReportProblem_Click(object sender, RoutedEventArgs e)
        {
            BugReportClicked?.Invoke(sender, e);
        }

        private void menuCheckForUpdate_Click(object sender, RoutedEventArgs e)
        {
            UpdateClicked?.Invoke(sender, e);
        }

        private void menuAbout_Click(object sender, RoutedEventArgs e)
        {
            var win = new AboutWindow
            {
                Owner = this
            };
            win.ShowDialog(this);
        }

        private void lvInProgress_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader column)
            {
                string sortBy = (string)column.Tag;
                if (string.IsNullOrEmpty(sortBy))
                {
                    return;
                }
                if (inProgressListViewSortCol != null)
                {
                    AdornerLayer.GetAdornerLayer(inProgressListViewSortCol).Remove(inProgressListViewSortAdorner);
                    lvInProgress.Items.SortDescriptions.Clear();
                }

                ListSortDirection newDir = ListSortDirection.Ascending;
                if (inProgressListViewSortCol == column && inProgressListViewSortAdorner?.Direction == newDir)
                    newDir = ListSortDirection.Descending;

                inProgressListViewSortCol = column;
                inProgressListViewSortAdorner = new SortAdorner(inProgressListViewSortCol, newDir);
                AdornerLayer.GetAdornerLayer(inProgressListViewSortCol).Add(inProgressListViewSortAdorner);
                lvInProgress.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
            }
        }

        private void CreateMenuItems()
        {
            menuItems = new IMenuItem[]
            {
                new MenuItemWrapper("pause",TextResource.GetText("MENU_PAUSE")),
                new MenuItemWrapper("resume",TextResource.GetText("MENU_RESUME")),
                new MenuItemWrapper("delete",TextResource.GetText("DESC_DEL")),
                new MenuItemWrapper("saveAs",TextResource.GetText("CTX_SAVE_AS")),
                new MenuItemWrapper("refresh",TextResource.GetText("MENU_REFRESH_LINK")),
                new MenuItemWrapper("showProgress",TextResource.GetText("LBL_SHOW_PROGRESS")),
                new MenuItemWrapper("copyURL",TextResource.GetText("CTX_COPY_URL")),
                new MenuItemWrapper("restart",TextResource.GetText("MENU_RESTART")),
                new MenuItemWrapper("moveToQueue",TextResource.GetText("Q_MOVE_TO")),
                new MenuItemWrapper("properties",TextResource.GetText("MENU_PROPERTIES")),

                new MenuItemWrapper("open",TextResource.GetText("CTX_OPEN_FILE")),
                new MenuItemWrapper("openFolder",TextResource.GetText("CTX_OPEN_FOLDER")),
                new MenuItemWrapper("deleteDownloads",TextResource.GetText("MENU_DELETE_DWN")),
                new MenuItemWrapper("copyURL1",TextResource.GetText("CTX_COPY_URL")),
                new MenuItemWrapper("copyFile",TextResource.GetText("CTX_COPY_FILE")),
                new MenuItemWrapper("downloadAgain",TextResource.GetText("MENU_RESTART")),
                new MenuItemWrapper("properties1",TextResource.GetText("MENU_PROPERTIES")),
                new MenuItemWrapper("schedule",TextResource.GetText("Q_SCHEDULE_TXT"),false)
            };

            var dict = new Dictionary<string, IMenuItem>();
            foreach (var mi in menuItems)
            {
                dict[mi.Name] = mi;
            }

            this.MenuItemMap = dict;

            var lvInProgressContextMenu = (ContextMenu)this.FindResource("lvInProgressContextMenu");
            var lvFinishedContextMenu = (ContextMenu)this.FindResource("lvFinishedContextMenu");
            var i = 0;
            foreach (MenuItemWrapper mi in menuItems)
            {
                if (i < 10)
                {
                    lvInProgressContextMenu.Items.Add(mi.Menu);
                }
                else
                {
                    lvFinishedContextMenu.Items.Add(mi.Menu);
                }
                i++;
            }
            lvInProgress.ContextMenuOpening += LvInProgressContextMenu_ContextMenuOpening;
            lvFinished.ContextMenuOpening += LvFinishedContextMenu_ContextMenuOpening;
            lvAll.ContextMenuOpening += LvAll_ContextMenuOpening;

            var newDownloadMenu = (ContextMenu)FindResource("newDownloadContextMenu");

            var menuNewDownload = (MenuItem)newDownloadMenu.Items[0];
            menuNewDownload.Click += MenuNewDownload_Click;
            menuNewDownload.Header = TextResource.GetText("LBL_NEW_DOWNLOAD");

            var menuVideoDownload = (MenuItem)newDownloadMenu.Items[1];
            menuVideoDownload.Click += MenuVideoDownload_Click;
            menuVideoDownload.Header = TextResource.GetText("LBL_VIDEO_DOWNLOAD");

            var menuBatchDownload = (MenuItem)newDownloadMenu.Items[2];
            menuBatchDownload.Click += MenuBatchDownload_Click;
            menuBatchDownload.Header = TextResource.GetText("MENU_BATCH_DOWNLOAD");

            var menuFileHostDownload = (MenuItem)newDownloadMenu.Items[3];
            menuFileHostDownload.Click += MenuFileHostDownload_Click;
            menuFileHostDownload.Header = TextResource.GetText("MENU_FILEHOST_DOWNLOAD");
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            if (BtnHelp.Tag != null)
            {
                UpdateClicked?.Invoke(sender, e);
            }
            else
            {
                HelpClicked?.Invoke(sender, e);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void ListViewItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DownloadListDoubleClicked?.Invoke(sender, e);
        }

        private void MenuNewDownload_Click(object sender, RoutedEventArgs e)
        {
            this.NewDownloadClicked?.Invoke(sender, e);
        }

        private void MenuVideoDownload_Click(object sender, RoutedEventArgs e)
        {
            this.YoutubeDLDownloadClicked?.Invoke(sender, e);
        }

        private void menuMediaGrabber_Click(object sender, RoutedEventArgs e)
        {
            ApplicationContext.PlatformUIService.CreateAndShowMediaGrabber();
        }

        //private void extRegister_Click(object sender, RoutedEventArgs e)
        //{
        //    ApplicationContext.PlatformUIService.ShowExtensionRegistrationWindow();
        //}

        private void MenuBatchDownload_Click(object sender, RoutedEventArgs e)
        {
            this.BatchDownloadClicked?.Invoke(sender, e);
        }

        private void MenuFileHostDownload_Click(object sender, RoutedEventArgs e)
        {
            ApplicationContext.PlatformUIService.ShowFileHostDownloadWindow();
        }

        private void LvFinishedContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            this.FinishedContextMenuOpening?.Invoke(sender, e);
        }

        private void LvInProgressContextMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            this.InProgressContextMenuOpening?.Invoke(sender, e);
        }

        private void LvAll_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Each row carries the menu of its kind; enable that menu's items for the selection.
            if ((e.OriginalSource as FrameworkElement)?.DataContext is AllDownloadsRow row && row.IsFinished)
            {
                this.FinishedContextMenuOpening?.Invoke(sender, e);
            }
            else
            {
                this.InProgressContextMenuOpening?.Invoke(sender, e);
            }
        }

        public IPlatformClipboardMonitor GetClipboardMonitor() => this.clipboarMonitor;

        public void ShowAndActivate()
        {
            this.Show();
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
        }
    }
}
