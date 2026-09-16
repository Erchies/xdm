using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using XDM.Core.FileHosts;
using XDM.Core.UI;
using XDM.Core.Util;
using XDM.Wpf.UI.Common.Helpers;
using XDM.Wpf.UI.Win32;

namespace XDM.Wpf.UI.Dialogs.FileHostDownload
{
    /// <summary>
    /// Interaction logic for FileHostDownloadWindow.xaml
    /// </summary>
    public partial class FileHostDownloadWindow : Window, IFileHostDownloadView
    {
        private const int MaxLogLines = 500;

        private readonly ObservableCollection<FileHostEntry> files = new();
        private readonly ObservableCollection<LogEntry> log = new();

        public FileHostDownloadWindow()
        {
            InitializeComponent();
            LvFiles.ItemsSource = files;
            LbLog.ItemsSource = log;
        }

        public string LinksText { get => TxtLinks.Text; set => TxtLinks.Text = value; }

        public string Password => TxtPassword.Password;

        public string DownloadLocation { get => TxtSaveIn.Text; set => TxtSaveIn.Text = value; }

        public bool CreateSubfolders { get => ChkSubfolders.IsChecked ?? false; set => ChkSubfolders.IsChecked = value; }

        public string GofileAccountToken { get => TxtGofileToken.Password; set => TxtGofileToken.Password = value; }

        public string GofileWebsiteTokenSalt { get => TxtGofileSalt.Text; set => TxtGofileSalt.Text = value; }

        public string BunkrIncludeFilter { get => TxtBunkrInclude.Text; set => TxtBunkrInclude.Text = value; }

        public string BunkrIgnoreFilter { get => TxtBunkrIgnore.Text; set => TxtBunkrIgnore.Text = value; }

        public bool BunkrCleanName { get => ChkBunkrCleanName.IsChecked ?? false; set => ChkBunkrCleanName.IsChecked = value; }

        public IEnumerable<ResolvedFile> SelectedFiles => files.Where(f => f.IsSelected).Select(f => f.File).ToList();

        public event EventHandler? BrowseClicked;
        public event EventHandler? ListClicked;
        public event EventHandler? StopClicked;
        public event EventHandler? DownloadClicked;
        public event EventHandler<DownloadLaterEventArgs>? DownloadLaterClicked;
        public event EventHandler? QueueSchedulerClicked;
        public event EventHandler? SelectionChanged;
        public event EventHandler? WindowClosed;

        public void SetFiles(IEnumerable<ResolvedFile> resolvedFiles)
        {
            foreach (var entry in files)
            {
                entry.PropertyChanged -= Entry_PropertyChanged;
            }
            files.Clear();
            foreach (var file in resolvedFiles)
            {
                var entry = new FileHostEntry(file);
                entry.PropertyChanged += Entry_PropertyChanged;
                files.Add(entry);
            }
            ChkSelectAll.IsChecked = true;
        }

        public void AppendLog(FileHostLogLevel level, string message)
        {
            if (log.Count >= MaxLogLines)
            {
                log.RemoveAt(0);
            }
            var entry = new LogEntry(level, message, TryFindResource("ControlForecolor") as Brush);
            log.Add(entry);
            LbLog.ScrollIntoView(entry);
        }

        public void ClearLog() => log.Clear();

        public void SetBusy(bool busy)
        {
            BtnStop.IsEnabled = busy;
            BtnList.IsEnabled = !busy;
            BtnDownloadNow.IsEnabled = !busy;
            BtnDownloadLater.IsEnabled = !busy;
            TabOptions.IsEnabled = !busy;
        }

        public void SetStatus(string text) => TxtStatus.Text = text;

        public string? SelectFolder()
        {
            using var fb = new System.Windows.Forms.FolderBrowserDialog();
            if (Directory.Exists(TxtSaveIn.Text))
            {
                fb.SelectedPath = TxtSaveIn.Text;
            }
            if (fb.ShowDialog(new WinformsWindow(this)) == System.Windows.Forms.DialogResult.OK)
            {
                return fb.SelectedPath;
            }
            return null;
        }

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileHostEntry.IsSelected))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var selected = ChkSelectAll.IsChecked ?? false;
            foreach (var entry in files)
            {
                entry.PropertyChanged -= Entry_PropertyChanged;
                entry.IsSelected = selected;
                entry.PropertyChanged += Entry_PropertyChanged;
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e) => BrowseClicked?.Invoke(this, EventArgs.Empty);

        private void BtnList_Click(object sender, RoutedEventArgs e) => ListClicked?.Invoke(this, EventArgs.Empty);

        private void BtnStop_Click(object sender, RoutedEventArgs e) => StopClicked?.Invoke(this, EventArgs.Empty);

        private void BtnDownloadNow_Click(object sender, RoutedEventArgs e) => DownloadClicked?.Invoke(this, EventArgs.Empty);

        private void BtnDownloadLater_Click(object sender, RoutedEventArgs e)
        {
            DownloadLaterMenuHelper.PopulateMenuAndAttachEvents(
                (s, args) => DownloadLaterClicked?.Invoke(this, args), BtnDownloadLater, this);
        }

        private void DontAddToQueueMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DownloadLaterClicked?.Invoke(this, new DownloadLaterEventArgs(null));
        }

        private void QueueAndSchedulerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            QueueSchedulerClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closed(object sender, EventArgs e) => WindowClosed?.Invoke(this, EventArgs.Empty);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
#if NET45_OR_GREATER
            if (App.Skin == Skin.Dark)
            {
                var helper = new WindowInteropHelper(this);
                helper.EnsureHandle();
                DarkModeHelper.UseImmersiveDarkMode(helper.Handle, true);
            }
#endif
        }

        private class FileHostEntry : INotifyPropertyChanged
        {
            private bool selected = true;

            public FileHostEntry(ResolvedFile file)
            {
                File = file;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public ResolvedFile File { get; }

            public string Name => File.Name;

            public string Folder => File.RelativeFolder;

            public string SizeText => File.Size > 0 ? FormattingHelper.FormatSize(File.Size) : "---";

            public string Host => File.Host;

            public bool IsSelected
            {
                get => selected;
                set
                {
                    if (selected != value)
                    {
                        selected = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    }
                }
            }
        }

        private class LogEntry
        {
            private static readonly Brush WarningBrush = CreateBrush(Color.FromRgb(0xD9, 0x8A, 0x00));
            private static readonly Brush ErrorBrush = CreateBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));

            public LogEntry(FileHostLogLevel level, string text, Brush? defaultBrush)
            {
                Text = $"{DateTime.Now:HH:mm:ss}  {text}";
                Brush = level switch
                {
                    FileHostLogLevel.Warning => WarningBrush,
                    FileHostLogLevel.Error => ErrorBrush,
                    _ => defaultBrush ?? SystemColors.ControlTextBrush
                };
            }

            public string Text { get; }

            public Brush Brush { get; }

            private static Brush CreateBrush(Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
    }
}
