using System;
using System.Collections.Generic;
using System.Linq;

namespace XDM.Core
{
    /// <summary>
    /// Live numbers of the downloads that are transferring right now, fed by the progress events
    /// of the download engine and read by the UI (speed column and status bar totals).
    /// </summary>
    public static class DownloadStats
    {
        /// <summary>
        /// A download that has not reported progress for this long counts as not transferring.
        /// </summary>
        public const int StaleMilliseconds = 3000;

        private class Entry
        {
            public double Speed;
            public long Downloaded;
            public long Size;
            public DateTime Updated;
        }

        private static readonly Dictionary<string, Entry> entries = new();
        private static readonly object sync = new();
        private static long sessionBytes;
        private static double peakSpeed;

        /// <summary>
        /// Clock used for staleness; replaceable in tests.
        /// </summary>
        public static Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        public static void Update(string id, double speed, long downloaded, long size)
        {
            lock (sync)
            {
                if (entries.TryGetValue(id, out var entry))
                {
                    if (downloaded > entry.Downloaded)
                    {
                        sessionBytes += downloaded - entry.Downloaded;
                    }
                }
                else
                {
                    // The first report of a resumed download includes what earlier sessions fetched,
                    // so it only sets the baseline.
                    entry = new Entry();
                    entries[id] = entry;
                }
                entry.Speed = Math.Max(0, speed);
                entry.Downloaded = downloaded;
                entry.Size = size;
                entry.Updated = Now();
            }
        }

        public static void Remove(string id)
        {
            lock (sync)
            {
                entries.Remove(id);
            }
        }

        /// <summary>
        /// Current speed of one download in bytes per second, 0 when it is not transferring.
        /// </summary>
        public static double GetSpeed(string id)
        {
            lock (sync)
            {
                return entries.TryGetValue(id, out var entry) && !IsStale(entry) ? entry.Speed : 0;
            }
        }

        public static DownloadStatsSnapshot GetSnapshot()
        {
            lock (sync)
            {
                var live = entries.Values.ToList();
                var totalSpeed = live.Where(e => !IsStale(e)).Sum(e => e.Speed);
                peakSpeed = Math.Max(peakSpeed, totalSpeed);
                return new DownloadStatsSnapshot
                {
                    TotalSpeed = totalSpeed,
                    Transferring = live.Count,
                    Remaining = live.Where(e => e.Size > 0).Sum(e => Math.Max(0, e.Size - e.Downloaded)),
                    UnknownSize = live.Count(e => e.Size <= 0),
                    SessionBytes = sessionBytes,
                    PeakSpeed = peakSpeed
                };
            }
        }

        /// <summary>
        /// Forgets everything, including the session totals. For tests.
        /// </summary>
        public static void Reset()
        {
            lock (sync)
            {
                entries.Clear();
                sessionBytes = 0;
                peakSpeed = 0;
            }
        }

        private static bool IsStale(Entry entry) => (Now() - entry.Updated).TotalMilliseconds > StaleMilliseconds;
    }

    public class DownloadStatsSnapshot
    {
        /// <summary>Bytes per second across all downloads.</summary>
        public double TotalSpeed { get; set; }

        /// <summary>Downloads that reported progress and have not finished, failed or stopped.</summary>
        public int Transferring { get; set; }

        /// <summary>Bytes still missing from the downloads whose size is known.</summary>
        public long Remaining { get; set; }

        /// <summary>Downloads without a known size, left out of <see cref="Remaining"/>.</summary>
        public int UnknownSize { get; set; }

        /// <summary>Bytes received since XDM started.</summary>
        public long SessionBytes { get; set; }

        /// <summary>Highest total speed seen since XDM started.</summary>
        public double PeakSpeed { get; set; }

        /// <summary>Seconds to fetch <see cref="Remaining"/> at the current speed, or -1.</summary>
        public long EtaSeconds => TotalSpeed > 0 && Remaining > 0 ? (long)Math.Ceiling(Remaining / TotalSpeed) : -1;
    }
}
