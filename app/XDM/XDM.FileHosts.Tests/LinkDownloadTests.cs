using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using XDM.Core;

namespace XDM.FileHosts.Tests
{
    [NonParallelizable]
    public class DownloadStatsTests
    {
        private DateTime now;

        [SetUp]
        public void Start()
        {
            now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
            DownloadStats.Now = () => now;
            DownloadStats.Reset();
        }

        [TearDown]
        public void Stop()
        {
            DownloadStats.Reset();
            DownloadStats.Now = () => DateTime.UtcNow;
        }

        [Test]
        public void SumsSpeedRemainingAndSessionBytes()
        {
            DownloadStats.Update("a", 1000, 500, 10_000);
            DownloadStats.Update("b", 3000, 0, -1);
            now = now.AddSeconds(1);
            DownloadStats.Update("a", 2000, 2500, 10_000);
            DownloadStats.Update("b", 1000, 4000, -1);

            var snapshot = DownloadStats.GetSnapshot();

            Assert.AreEqual(3000, snapshot.TotalSpeed);
            Assert.AreEqual(2, snapshot.Transferring);
            Assert.AreEqual(7500, snapshot.Remaining, "only downloads with a known size");
            Assert.AreEqual(1, snapshot.UnknownSize);
            Assert.AreEqual(2000 + 4000, snapshot.SessionBytes, "first report of each download is the baseline");
            Assert.AreEqual(3, snapshot.EtaSeconds);
            Assert.AreEqual(2000, DownloadStats.GetSpeed("a"));
        }

        [Test]
        public void SilentDownloadsStopCountingSpeedAndRemovedOnesDisappear()
        {
            DownloadStats.Update("a", 5000, 100, 1000);
            DownloadStats.Update("b", 7000, 100, 1000);
            Assert.AreEqual(12000, DownloadStats.GetSnapshot().PeakSpeed);

            now = now.AddMilliseconds(DownloadStats.StaleMilliseconds + 1);
            DownloadStats.Update("b", 1000, 200, 1000);
            DownloadStats.Remove("missing");

            var snapshot = DownloadStats.GetSnapshot();
            Assert.AreEqual(1000, snapshot.TotalSpeed);
            Assert.AreEqual(0, DownloadStats.GetSpeed("a"));
            Assert.AreEqual(12000, snapshot.PeakSpeed, "peak is kept");

            DownloadStats.Remove("a");
            DownloadStats.Remove("b");
            snapshot = DownloadStats.GetSnapshot();
            Assert.AreEqual(0, snapshot.Transferring);
            Assert.AreEqual(-1, snapshot.EtaSeconds);
            Assert.AreEqual(100, snapshot.SessionBytes, "session total survives finished downloads");
        }

        [Test]
        public void DownloadedGoingBackwardsIsNotCounted()
        {
            DownloadStats.Update("a", 10, 1000, 5000);
            DownloadStats.Update("a", 10, 400, 5000);
            DownloadStats.Update("a", 10, 900, 5000);

            Assert.AreEqual(500, DownloadStats.GetSnapshot().SessionBytes);
        }
    }

    public class DuplicateDownloadCheckerTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "xdm-dup-root");

        private static string AutoFolder(string name) => Path.Combine(Root, name.EndsWith(".mp4") ? "Video" : "Other");

        [Test]
        public void SkipsExistingFilesUnfinishedDownloadsAndRepeats()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Root, "Album", "a.mp4") };
            var unfinished = new[]
            {
                new KeyValuePair<string?, string>(Path.Combine(Root, "Album"), "b.mp4"),
                new KeyValuePair<string?, string>(null, "auto.mp4")
            };
            var checker = new DuplicateDownloadChecker(unfinished, AutoFolder, existing.Contains);

            Assert.AreEqual(DuplicateReason.FileExists, checker.Check("A.MP4", Path.Combine(Root, "Album"), out var path));
            Assert.AreEqual(Path.Combine(Root, "Album", "A.MP4"), path);
            Assert.AreEqual(DuplicateReason.AlreadyInList, checker.Check("b.mp4", Path.Combine(Root, "Album") + Path.DirectorySeparatorChar, out _));
            Assert.AreEqual(DuplicateReason.AlreadyInList, checker.Check("auto.mp4", Path.Combine(Root, "Video"), out _),
                "a download without folder lands in the folder of its file type");
            Assert.AreEqual(DuplicateReason.None, checker.Check("c.mp4", Path.Combine(Root, "Album"), out _));
            Assert.AreEqual(DuplicateReason.RepeatedInBatch, checker.Check("c.mp4", Path.Combine(Root, "Album"), out _));
            Assert.AreEqual(DuplicateReason.None, checker.Check("c.mp4", Path.Combine(Root, "Other album"), out _),
                "same name in another folder is a different file");
        }

        [Test]
        public void UnusableNamesAreNotReportedAsDuplicates()
        {
            var checker = new DuplicateDownloadChecker(Enumerable.Empty<KeyValuePair<string?, string>>(), _ => string.Empty, _ => true);

            Assert.AreEqual(DuplicateReason.None, checker.Check("", Root, out var path));
            Assert.IsNull(path);
            Assert.AreEqual(DuplicateReason.None, checker.Check("x.bin", null, out _), "no folder could be worked out");
        }
    }

    public class SkippedDownloadListTests
    {
        [Test]
        public void AppendsOneTabSeparatedLinePerSkippedDownload()
        {
            var file = Path.Combine(Path.GetTempPath(), "xdm-skipped-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                SkippedDownloadList.Append(file, new DateTime(2026, 9, 16, 21, 5, 3), "o arquivo já existe", @"C:\D\vídeo.mp4", "https://x.test/a?token=1");
                SkippedDownloadList.Append(file, new DateTime(2026, 9, 16, 21, 5, 4), "repetido", "C:\\D\\b\tc.mp4", null);

                var bytes = File.ReadAllBytes(file);
                Assert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "BOM written once, at the start");
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                Assert.AreEqual(2, lines.Length);
                Assert.AreEqual("2026-09-16 21:05:03\to arquivo já existe\tC:\\D\\vídeo.mp4\thttps://x.test/a?token=1", lines[0]);
                Assert.AreEqual("2026-09-16 21:05:04\trepetido\tC:\\D\\b c.mp4\t", lines[1]);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
