using System;
using System.ComponentModel;
using Translations;

namespace XDM.Wpf.UI
{
    /// <summary>
    /// Row of the "All" list: shows an unfinished or a finished download with the same columns,
    /// so both kinds sort and filter together. Property changes of the wrapped row pass through.
    /// </summary>
    internal class AllDownloadsRow : INotifyPropertyChanged
    {
        public AllDownloadsRow(InProgressDownloadEntryWrapper row)
        {
            InProgress = row;
            row.PropertyChanged += Row_PropertyChanged;
        }

        public AllDownloadsRow(FinishedDownloadEntryWrapper row)
        {
            Finished = row;
            row.PropertyChanged += Row_PropertyChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public InProgressDownloadEntryWrapper? InProgress { get; }

        public FinishedDownloadEntryWrapper? Finished { get; }

        public object Source => (object?)InProgress ?? Finished!;

        public bool IsFinished => Finished != null;

        public string Name => InProgress?.Name ?? Finished!.Name;

        public DateTime DateAdded => InProgress?.DateAdded ?? Finished!.DateAdded;

        public long Size => InProgress?.Size ?? Finished!.Size;

        public int Progress => InProgress?.Progress ?? 100;

        public string StateText => InProgress?.StateText ?? TextResource.GetText("STAT_FINISHED");

        public string SpeedText => InProgress?.SpeedText ?? string.Empty;

        public string EtaText => InProgress?.EtaText ?? string.Empty;

        public double SpeedValue => InProgress?.SpeedValue ?? 0;

        /// <summary>
        /// Stops listening to the wrapped row once this row leaves the list.
        /// </summary>
        public void Detach()
        {
            if (InProgress != null)
            {
                InProgress.PropertyChanged -= Row_PropertyChanged;
            }
            if (Finished != null)
            {
                Finished.PropertyChanged -= Row_PropertyChanged;
            }
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }
    }
}
