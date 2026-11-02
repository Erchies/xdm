using System;
using System.Linq;
using NUnit.Framework;
using XDM.Core.FileHosts;

namespace XDM.FileHosts.Tests
{
    public class EromeResolverTests
    {
        private const string AlbumUrl = "https://www.erome.com/a/AbCd1234";

        // Shaped like the album fixture of M4p4/EromeDownloader, plus the markup around it.
        private const string AlbumPage = @"<!DOCTYPE html>
<html><head>
<meta property=""og:title"" content=""Praia &amp; Sol"">
<meta property=""og:description"" content=""ignored"">
<script>var items = [""<source src='https://cdn.erome.com/fake.mp4'>""]; if (a<b) { x(); }</script>
</head><body>
<div class=""media-group"">
  <video preload=""none"" poster=""https://cdn.erome.com/thumb.jpg"">
    <source src=""https://s11.erome.com/123/AbCd1234/video1.mp4"" type=""video/mp4"">
  </video>
  <video><source src=""https://s11.erome.com/123/AbCd1234/video2.mp4"" type=""video/mp4""></video>
  <div class=""img-backdrop""></div>
  <div class=""img"" data-src=""https://s11.erome.com/123/AbCd1234/image1.jpg""></div>
  <div class=""media-group img lazyload"" data-src=""https://s11.erome.com/123/AbCd1234/image2.jpg""></div>
  <div class=""img"" data-src=""https://s11.erome.com/123/AbCd1234/video1.mp4""></div>
  <div class=""img"" data-src=""""></div>
  <div class=""img"" data-src=""/123/AbCd1234/image3.jpg""></div>
</div>
</body></html>";

        private static FileHostOptions Options() => new() { RetryBaseDelayMilliseconds = 0 };

        private static FakeHttp Album(string html = AlbumPage) =>
            new FakeHttp().On("GET", AlbumUrl, 200, html);

        [TestCase("https://www.erome.com/a/AbCd1234", true)]
        [TestCase("http://www.erome.com/a/AbCd1234/", true)]
        [TestCase("erome.com/a/AbCd1234", true)]
        [TestCase("https://erome.com/a/AbCd1234?utm=x", true)]
        [TestCase("https://www.erome.com/someuser", false)]
        [TestCase("https://www.erome.com/a/", false)]
        [TestCase("https://www.erome.com/", false)]
        [TestCase("https://noterome.com/a/AbCd1234", false)]
        [TestCase("https://erome.com.evil.test/a/AbCd1234", false)]
        [TestCase("", false)]
        public void DetectsAlbumLinks(string url, bool supported)
        {
            Assert.AreEqual(supported, EromeResolver.IsEromeLink(url));
            Assert.AreEqual(supported, FileHostResolvers.IsSupported(url));
        }

        [Test]
        public void ReadsAlbumTitle()
        {
            Assert.AreEqual("Praia & Sol", EromeResolver.GetAlbumTitle(AlbumPage));
            Assert.IsNull(EromeResolver.GetAlbumTitle("<html><head><title>x</title></head></html>"));
            Assert.IsNull(EromeResolver.GetAlbumTitle(@"<meta property=""og:title"" content="" "">"));
        }

        [Test]
        public void CollectsVideosAndImagesInOrderWithoutRepeats()
        {
            var urls = EromeResolver.GetMediaUrls(AlbumPage, AlbumUrl);

            CollectionAssert.AreEqual(new[]
            {
                "https://s11.erome.com/123/AbCd1234/video1.mp4",
                "https://s11.erome.com/123/AbCd1234/video2.mp4",
                "https://s11.erome.com/123/AbCd1234/image1.jpg",
                "https://s11.erome.com/123/AbCd1234/image2.jpg",
                "https://www.erome.com/123/AbCd1234/image3.jpg"
            }, urls);
        }

        [Test]
        public void ResolvesAlbumIntoDownloadsWithRefererHeader()
        {
            var http = Album();

            var files = new EromeResolver(http, Options()).Resolve(new FileHostLink("erome.com/a/AbCd1234", null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { "video1.mp4", "video2.mp4", "image1.jpg", "image2.jpg", "image3.jpg" },
                files.Select(f => f.Name));
            Assert.IsTrue(files.All(f => f.RelativeFolder == "Praia & Sol"));
            Assert.IsTrue(files.All(f => f.Host == "Erome"));
            Assert.IsTrue(files.All(f => f.Size == -1), "the album page does not report sizes");
            Assert.AreEqual("erome.com/a/AbCd1234", files[0].SourceUrl);
            CollectionAssert.AreEqual(new[] { AlbumUrl }, files[0].Headers["Referer"]);
            CollectionAssert.AreEqual(new[] { EromeResolver.UserAgent }, files[0].Headers["User-Agent"]);
            Assert.AreEqual(AlbumUrl, http.Requests.Single().Url, "a bare erome.com link is normalised to www");
        }

        [Test]
        public void RefererFollowsARedirectedPage()
        {
            var redirected = "https://www.erome.com/a/AbCd1234?lang=en";
            var http = new FakeHttp().On("GET", _ => true,
                _ => new FileHostHttpResponse(200, AlbumPage, new Uri(redirected)));

            var files = new EromeResolver(http, Options()).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { redirected }, files[0].Headers["Referer"]);
        }

        [Test]
        public void NoSubfolderWhenTurnedOff()
        {
            var options = Options();
            options.CreateSubfolders = false;

            var files = new EromeResolver(Album(), options).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext());

            Assert.IsTrue(files.All(f => f.RelativeFolder == string.Empty));
        }

        [Test]
        public void FallsBackToTheAlbumIdWhenThereIsNoTitle()
        {
            var html = @"<html><body><video><source src=""https://s1.erome.com/a.mp4""></video></body></html>";

            var files = new EromeResolver(Album(html), Options()).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext());

            Assert.AreEqual("AbCd1234", files.Single().RelativeFolder);
        }

        [Test]
        public void RepeatedFileNamesGetASuffix()
        {
            var html = @"<html><body>
<video><source src=""https://s1.erome.com/111/clip.mp4""></video>
<video><source src=""https://s2.erome.com/222/clip.mp4""></video>
</body></html>";

            var files = new EromeResolver(Album(html), Options()).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { "clip.mp4", "clip(1).mp4" }, files.Select(f => f.Name));
        }

        [Test]
        public void AppliesTheNameFilters()
        {
            var options = Options();
            options.Ignore = new[] { "video2" };
            options.Include = new[] { ".mp4" };
            var skipped = 0;
            var context = new FileHostContext((level, _) => { if (level == FileHostLogLevel.Info) skipped++; });

            var files = new EromeResolver(Album(), options).Resolve(new FileHostLink(AlbumUrl, null), context);

            CollectionAssert.AreEqual(new[] { "video1.mp4" }, files.Select(f => f.Name));
            Assert.AreEqual(5, skipped, "one album line plus four skipped files");
        }

        [Test]
        public void AlbumWithoutMediaIsAnError()
        {
            var http = Album(@"<html><head><meta property=""og:title"" content=""Empty""></head><body></body></html>");

            Assert.Throws<FileHostException>(() =>
                new EromeResolver(http, Options()).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext()));
        }

        [Test]
        public void PageErrorsAreReported()
        {
            var http = new FakeHttp().On("GET", AlbumUrl, 404, "<html>not found</html>");

            var ex = Assert.Throws<FileHostException>(() =>
                new EromeResolver(http, Options()).Resolve(new FileHostLink(AlbumUrl, null), new FileHostContext()));

            StringAssert.Contains("404", ex!.Message);
        }

        [Test]
        public void UnsupportedLinkIsRejectedWithoutARequest()
        {
            var http = Album();

            Assert.Throws<FileHostException>(() =>
                new EromeResolver(http, Options()).Resolve(new FileHostLink("https://www.erome.com/someuser", null), new FileHostContext()));
            Assert.IsEmpty(http.Requests);
        }

        [Test]
        public void ResolverSetPicksEromeForEromeLinks()
        {
            var resolvers = FileHostResolvers.Create(new FakeHttp(), Options());

            Assert.AreEqual("Erome", FileHostResolvers.Find(resolvers, AlbumUrl)?.Name);
            Assert.AreEqual("Bunkr", FileHostResolvers.Find(resolvers, "https://bunkr.si/a/x")?.Name);
            Assert.AreEqual("GoFile", FileHostResolvers.Find(resolvers, "https://gofile.io/d/abcd")?.Name);
            Assert.IsNull(FileHostResolvers.Find(resolvers, "https://example.com/a/x"));
        }
    }

    public class FileHostOptionsFilterTests
    {
        [Test]
        public void IgnoreWinsOverInclude()
        {
            var options = new FileHostOptions { Include = new[] { "mp4" }, Ignore = new[] { "sample" } };

            Assert.IsFalse(options.ShouldSkip("movie.mp4"));
            Assert.IsTrue(options.ShouldSkip("sample.mp4"));
            Assert.IsTrue(options.ShouldSkip("photo.jpg"));
        }

        [Test]
        public void FiltersIgnoreCase()
        {
            var options = new FileHostOptions { Ignore = new[] { "TRAILER" } };

            Assert.IsTrue(options.ShouldSkip("the-trailer.mp4"));
        }

        [Test]
        public void NoFiltersKeepsEverything()
        {
            Assert.IsFalse(new FileHostOptions().ShouldSkip("anything.bin"));
        }
    }
}
