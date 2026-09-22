using System;
using System.ComponentModel;
using Translations;
using XDM.Core.UI;
using XDM.Core;
using XDM.Core.Util;

namespace XDM.Wpf.UI
{
    internal class InProgressDownloadEntryWrapper : INotifyPropertyChanged, IInProgressDownloadRow
    {
        private readonly InProgressDownloadItem entry;

        public event PropertyChangedEventHandler PropertyChanged;

        public InProgressDownloadEntryWrapper(InProgressDownloadItem entry)
        {
            this.entry = entry;
        }

        public string Name
        {
            get { return entry.Name; }
            set
            {
                entry.Name = value;
                OnPropertyChanged("Name");
            }
        }

        public long Size
        {
            get { return entry.Size; }
            set
            {
                entry.Size = value;
                OnPropertyChanged("Size");
            }
        }

        public DateTime DateAdded
        {
            get { return entry.DateAdded; }
            set
            {
                entry.DateAdded = value;
                OnPropertyChanged("DateAdded");
            }
        }

        public int Progress
        {
            get { return entry.Progress; }
            set
            {
                entry.Progress = value;
                OnPropertyChanged("Progress");
                OnStatusChanged();
            }
        }

        public string StatusText => Helpers.GenerateStatusText(this.entry);

        /// <summary>
        /// Status alone; speed and time left have their own columns.
        /// </summary>
        public string StateText => entry.Status switch
        {
            DownloadStatus.Downloading => TextResource.GetText("STAT_DOWNLOADING"),
            DownloadStatus.Stopped => TextResource.GetText("STAT_STOPPED"),
            DownloadStatus.Finished => TextResource.GetText("STAT_FINISHED"),
            DownloadStatus.Waiting => TextResource.GetText("STAT_WAITING"),
            _ => entry.Status.ToString()
        };

        public string SpeedText => entry.Status == DownloadStatus.Downloading ? entry.DownloadSpeed ?? string.Empty : string.Empty;

        public string EtaText => entry.Status == DownloadStatus.Downloading ? entry.ETA ?? string.Empty : string.Empty;

        /// <summary>
        /// Bytes per second, for sorting by speed.
        /// </summary>
        public double SpeedValue => entry.Status == DownloadStatus.Downloading ? DownloadStats.GetSpeed(entry.Id) : 0;

        public InProgressDownloadItem DownloadEntry => this.entry;

        public string FileIconText => IconMap.GetVectorNameForFileType(entry.Name);

        public DownloadStatus Status
        {
            get => entry.Status;
            set
            {
                entry.Status = value;
                OnStatusChanged();
            }
        }

        public string DownloadSpeed
        {
            get => entry.DownloadSpeed ?? string.Empty;
            set
            {
                entry.DownloadSpeed = value;
                OnStatusChanged();
            }
        }

        public string ETA
        {
            get => entry.ETA ?? string.Empty;
            set
            {
                entry.ETA = value;
                OnStatusChanged();
            }
        }

        /// <summary>
        /// This needs to be called after updating download speed or stopping the download
        /// </summary>
        public void UpdateStatusText()
        {
            OnStatusChanged();
        }

        private void OnStatusChanged()
        {
            OnPropertyChanged("Status");
            OnPropertyChanged("StatusText");
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(SpeedValue));
        }

        private void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
