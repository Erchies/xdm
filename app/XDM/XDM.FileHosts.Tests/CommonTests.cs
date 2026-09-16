using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using XDM.Core.FileHosts;

namespace XDM.FileHosts.Tests
{
    public class FileHostNamesTests
    {
        [TestCase("../../evil.exe", "evil.exe")]
        [TestCase(@"C:\Windows\system32\x.dll", "x.dll")]
        [TestCase("a<b>:c|d?.txt", "a_b__c_d_.txt")]
        [TestCase("CON.txt", "_CON.txt")]
        [TestCase("lpt1", "_lpt1")]
        [TestCase("name. . ", "name")]
        [TestCase("..", "unnamed")]
        [TestCase("   ", "unnamed")]
        [TestCase(null, "unnamed")]
        public void SanitizeProducesSafeNames(string? raw, string expected)
        {
            Assert.AreEqual(expected, FileHostNames.Sanitize(raw));
        }

        [Test]
        public void SanitizeCapsLengthKeepingExtension()
        {
            var name = FileHostNames.Sanitize(new string('a', 400) + ".mkv");
            Assert.AreEqual(FileHostNames.MaxNameLength, name.Length);
            StringAssert.EndsWith(".mkv", name);
        }

        [Test]
        public void RegistryAddsSuffixesIgnoringCase()
        {
            var registry = new FileHostNameRegistry();
            Assert.AreEqual("a.txt", registry.Reserve("root", "a.txt"));
            Assert.AreEqual("A(1).txt", registry.Reserve("root", "A.txt"));
            Assert.AreEqual("a(2).txt", registry.Reserve("root", "a.txt"));
            Assert.AreEqual("a.txt", registry.Reserve("other", "a.txt"));
            Assert.AreEqual("dir", registry.Reserve("root", "dir", isFolder: true));
            Assert.AreEqual("dir(1)", registry.Reserve("root", "dir", isFolder: true));
        }

        [Test]
        public void RegistrySkipsSuffixAlreadyTaken()
        {
            var registry = new FileHostNameRegistry();
            registry.Reserve("", "a(1).txt");
            registry.Reserve("", "a.txt");
            Assert.AreEqual("a(2).txt", registry.Reserve("", "a.txt"));
        }

        [Test]
        public void CombineWithinAcceptsSubfoldersAndRejectsEscapes()
        {
            var root = Path.Combine(Path.GetTempPath(), "xdm-filehost-root");
            Assert.AreEqual(Path.GetFullPath(root), FileHostNames.CombineWithin(root, ""));
            Assert.AreEqual(Path.Combine(Path.GetFullPath(root), "album", "sub"),
                FileHostNames.CombineWithin(root, FileHostNames.JoinRelative("album", "sub")));
            Assert.Throws<FileHostException>(() => FileHostNames.CombineWithin(root, Path.Combine("..", "outside")));
            Assert.Throws<FileHostException>(() => FileHostNames.CombineWithin(root, Path.Combine("..", "xdm-filehost-root-evil")));
        }
    }

    public class FileHostLinkParserTests
    {
        [Test]
        public void ParsesLinksWithOptionalPasswords()
        {
            var text = "https://gofile.io/d/aaaaaa senha desse link\r\n"
                + "\r\n"
                + "# comment\n"
                + "  https://gofile.io/d/bbbbbb  \n"
                + "https://gofile.io/d/aaaaaa other\n"
                + "https://bunkr.si/a/album1\tpw";

            var links = FileHostLinkParser.Parse(text, "default");

            Assert.AreEqual(3, links.Count);
            Assert.AreEqual("https://gofile.io/d/aaaaaa", links[0].Url);
            Assert.AreEqual("senha desse link", links[0].Password);
            Assert.AreEqual("https://gofile.io/d/bbbbbb", links[1].Url);
            Assert.AreEqual("default", links[1].Password);
            Assert.AreEqual("pw", links[2].Password);
        }

        [Test]
        public void EmptyDefaultPasswordBecomesNull()
        {
            var links = FileHostLinkParser.Parse("https://gofile.io/d/abcd", "");
            Assert.IsNull(links.Single().Password);
        }

        [TestCase("https://gofile.io/d/abc123", true)]
        [TestCase("gofile.io/d/abc123", true)]
        [TestCase("https://store9.gofile.io/download/web/abc/file.zip", false)]
        [TestCase("https://bunkr.si/a/AbCd1234", true)]
        [TestCase("https://bunkr.cr/f/video-AbCd.mp4", true)]
        [TestCase("https://bunkrr.su/v/xyz", true)]
        [TestCase("bunkr.fi/i/xyz", true)]
        [TestCase("https://bunkr.si/", false)]
        [TestCase("https://bunkr.si/albums", false)]
        [TestCase("https://notbunkr.com/a/xyz", false)]
        [TestCase("https://example.com/a/xyz", false)]
        [TestCase("", false)]
        public void DetectsSupportedLinks(string url, bool supported)
        {
            Assert.AreEqual(supported, FileHostResolvers.IsSupported(url));
        }

        [Test]
        public void ParsesFilterWords()
        {
            CollectionAssert.AreEqual(new[] { "mp4", "sample clip", "x" }, FileHostResolvers.ParseFilter(" mp4, sample clip;;x ,"));
            CollectionAssert.IsEmpty(FileHostResolvers.ParseFilter(null));
        }
    }

    public class FileHostRequestTests
    {
        [Test]
        public void RetriesNetworkErrorsAndServerErrors()
        {
            var calls = 0;
            var http = new FakeHttp().On("GET", _ => true, _ =>
            {
                calls++;
                if (calls == 1) throw new FileHostNetworkException("reset", new IOException());
                if (calls == 2) return new FileHostHttpResponse(503, "busy");
                return new FileHostHttpResponse(200, "ok");
            });
            var options = new FileHostOptions { Retries = 3, RetryBaseDelayMilliseconds = 0 };

            var response = FileHostRequest.Send(http, new FileHostContext(), options, "GET", "https://x.test/");

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(3, calls);
        }

        [Test]
        public void GivesUpAfterConfiguredAttempts()
        {
            var http = new FakeHttp().On("GET", _ => true, _ => throw new FileHostNetworkException("down", new IOException()));
            var options = new FileHostOptions { Retries = 2, RetryBaseDelayMilliseconds = 0 };

            Assert.Throws<FileHostException>(() =>
                FileHostRequest.Send(http, new FileHostContext(), options, "GET", "https://x.test/"));
            Assert.AreEqual(2, http.Requests.Count);
        }

        [Test]
        public void ClientErrorsAreReturnedWithoutRetry()
        {
            var http = new FakeHttp().On("GET", "https://x.test/", 404, "missing");
            var options = new FileHostOptions { Retries = 5, RetryBaseDelayMilliseconds = 0 };

            var response = FileHostRequest.Send(http, new FileHostContext(), options, "GET", "https://x.test/");

            Assert.AreEqual(404, response.StatusCode);
            Assert.AreEqual(1, http.Requests.Count);
        }

        [Test]
        public void CancelledContextStopsBeforeSending()
        {
            var http = new FakeHttp().On("GET", "https://x.test/", 200, "ok");
            var context = new FileHostContext();
            context.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                FileHostRequest.Send(http, context, new FileHostOptions(), "GET", "https://x.test/"));
            Assert.IsEmpty(http.Requests);
        }
    }

    public class FileHostFileDatesTests
    {
        private string dataDir = string.Empty;

        [SetUp]
        public void CreateDataDir()
        {
            dataDir = Path.Combine(Path.GetTempPath(), "xdm-filedates-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDir);
        }

        [TearDown]
        public void DeleteDataDir() => Directory.Delete(dataDir, true);

        [Test]
        public void SavedDateBecomesTheModifiedDateOnce()
        {
            var file = Path.Combine(dataDir, "video.mp4");
            File.WriteAllText(file, "x");
            var uploaded = new DateTime(2024, 3, 15, 10, 22, 3, DateTimeKind.Utc);

            FileHostFileDates.Save(dataDir, "id1", uploaded);

            Assert.IsTrue(FileHostFileDates.Apply(dataDir, "id1", file));
            Assert.AreEqual(uploaded, File.GetLastWriteTimeUtc(file));
            Assert.IsFalse(File.Exists(FileHostFileDates.PathFor(dataDir, "id1")), "the date file is consumed");
            Assert.IsFalse(FileHostFileDates.Apply(dataDir, "id1", file));
        }

        [Test]
        public void MissingOrBrokenDatesLeaveTheFileAlone()
        {
            var file = Path.Combine(dataDir, "a.bin");
            File.WriteAllText(file, "x");
            var before = File.GetLastWriteTimeUtc(file);
            File.WriteAllText(FileHostFileDates.PathFor(dataDir, "broken"), "not a date");

            Assert.IsFalse(FileHostFileDates.Apply(dataDir, "none", file));
            Assert.IsFalse(FileHostFileDates.Apply(dataDir, "broken", file));
            Assert.IsFalse(File.Exists(FileHostFileDates.PathFor(dataDir, "broken")));
            Assert.AreEqual(before, File.GetLastWriteTimeUtc(file));

            FileHostFileDates.Save(dataDir, "gone", DateTime.UtcNow);
            Assert.IsFalse(FileHostFileDates.Apply(dataDir, "gone", Path.Combine(dataDir, "missing.bin")));
        }
    }
}
