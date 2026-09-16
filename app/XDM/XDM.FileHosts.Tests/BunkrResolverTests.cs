using System;
using System.Linq;
using NUnit.Framework;
using XDM.Core.FileHosts;

namespace XDM.FileHosts.Tests
{
    public class BunkrHtmlTests
    {
        private const string AlbumPage = @"<!DOCTYPE html>
<html><head><title>Album</title>
<script>for (var i = 0; i<n; i++) { if (a > b) { x = ""<a class='after:absolute after:z-10 after:inset-0' href='/f/fake'>""; } }</script>
</head><body>
<!-- <a class=""after:absolute after:z-10 after:inset-0"" href=""/f/commented""> -->
<div class=""text-subs font-semibold flex text-base sm:text-lg"" x-data=""{ open: false }"">
  <h1 class=""truncate"">Caf&#195;&#169; &amp; Friends</h1>
</div>
<div class=""grid"">
  <div class=""theItem"" @click=""() => open = !open"">
    <a class=""after:absolute after:z-10 after:inset-0"" href=""/f/item-one""></a>
  </div>
  <div class=""theItem"">
    <a href=""/f/item-two"" data-x='1' class=""after:inset-0 after:absolute after:z-10""></a>
  </div>
  <a class=""after:absolute after:z-10 after:inset-0"" href=""/f/item-one""></a>
  <a class=""other"" href=""/f/not-an-item""></a>
</div>
<nav class=""pagination flex""><a href=""?page=1"">1</a><a href=""?page=2"">2</a><a href=""?page=3"">3</a><span>Next</span></nav>
</body></html>";

        private const string ItemPage = @"<html><head>
<script src=""/js/app.js"" data-file-id=""should-not-be-used-when-vars-exist""></script>
<script>
  var jsSlug = 'video-AbC';
  var jsCDN = ""https:\/\/c.bunkr-cache.se\/video-AbC.mp4"";
  var jsExt = '.mp4';
</script></head><body>
<h1 class=""text-subs font-semibold text-base sm:text-lg truncate"">clip by <a href=""/cdn-cgi/l/email-protection"" class=""__cf_email__"" data-cfemail=""5a373f1a29332e3f74393537"">[email&#160;protected]</a></h1>
</body></html>";

        [Test]
        public void ExtractsAlbumItemsIgnoringScriptsAndComments()
        {
            var pages = BunkrHtml.GetItemPages(AlbumPage, "https://bunkr.si");

            CollectionAssert.AreEqual(new[] { "https://bunkr.si/f/item-one", "https://bunkr.si/f/item-two" }, pages);
        }

        [Test]
        public void ReadsPaginationAndAlbumName()
        {
            Assert.AreEqual(3, BunkrHtml.GetPageCount(AlbumPage));
            Assert.AreEqual(1, BunkrHtml.GetPageCount("<html><body>no pages 42</body></html>"));
            Assert.AreEqual("Café & Friends", BunkrHtml.GetAlbumName(AlbumPage));
            Assert.IsNull(BunkrHtml.GetAlbumName("<div class='text-subs'><h1>x</h1></div>"));
        }

        [Test]
        public void ReadsItemNameDecodingCloudflareEmail()
        {
            Assert.AreEqual("clip by me@site.com", BunkrHtml.GetItemFileName(ItemPage));
        }

        [Test]
        public void ReadsPageVariables()
        {
            var vars = BunkrHtml.GetPageVariables(ItemPage);

            Assert.AreEqual("https://c.bunkr-cache.se/video-AbC.mp4", vars["jsCDN"]);
            Assert.AreEqual("video-AbC", vars["jsSlug"]);
            Assert.AreEqual(".mp4", vars["jsExt"]);
            Assert.AreEqual("should-not-be-used-when-vars-exist", BunkrHtml.GetFileId(ItemPage));
            Assert.AreEqual("abc_1-2", BunkrHtml.GetMediaSlug("<script>const slug = \"abc_1-2\";</script>"));
        }

        [Test]
        public void DecodesCloudflareEmail()
        {
            Assert.AreEqual("me@site.com", BunkrHtml.DecodeCloudflareEmail("5a373f1a29332e3f74393537"));
            Assert.Throws<FormatException>(() => BunkrHtml.DecodeCloudflareEmail("abc"));
        }

        [TestCase("CafÃ©", "Café")]
        [TestCase("Café", "Café")]
        [TestCase("plain", "plain")]
        [TestCase("日本語", "日本語")]
        public void FixesMojibakeOnly(string input, string expected)
        {
            Assert.AreEqual(expected, BunkrHtml.FixMojibake(input));
        }

        [Test]
        public void ReadsServerStatus()
        {
            var html = @"<div class=""flex items-center gap-4 py-4 border-b border-soft last:border-b-0""><p class=""x"">Nachos</p><span>Operational</span></div>
                         <div class=""flex items-center gap-4 py-4 border-b border-soft last:border-b-0""><p>Kebab</p><span class=""red"">Non-operational</span></div>";

            var status = BunkrHtml.GetServerStatus(html);

            Assert.AreEqual("Operational", status["nachos"]);
            Assert.AreEqual("Non-operational", status["Kebab"]);
        }

        [TestCase("video.mp4", "video.mp4", "video.mp4")]
        [TestCase("video.mp4", "video-AbCd1234.mp4", "video-AbCd1234.mp4")]
        [TestCase("video.mp4", "video_AbCd1234.mp4", "video_AbCd1234.mp4")]
        [TestCase("video.mp4", "video.mkv", "video.mkv")]
        [TestCase("clip.mp4", "myclip-XyZ.mp4", "clip-myclip-XyZ.mp4")]
        [TestCase("clip.mp4", "clipper-XyZ.mp4", "clip-clipper-XyZ.mp4")]
        [TestCase("My: Clip.mp4", "clip-XyZ.mp4", "My Clip-clip-XyZ.mp4")]
        [TestCase("noext", "file-123.bin", "noext-file-123")]
        public void FormatsItemFileNamesLikeTheOriginalCrawler(string original, string urlName, string expected)
        {
            Assert.AreEqual(expected, BunkrResolver.FormatItemFileName(original, urlName));
        }

        [TestCase("2024-03-15T10:22:03Z", "2024-03-15T10:22:03")]
        [TestCase("2024-03-15T10:22:03.5+02:00", "2024-03-15T08:22:03.5")]
        [TestCase("2024-03-15 10:22:03", "2024-03-15T10:22:03")]
        [TestCase("2024-03-15", "2024-03-15T00:00:00")]
        [TestCase("15/03/2024", "2024-03-15T00:00:00")]
        [TestCase("March 15, 2024", "2024-03-15T00:00:00")]
        [TestCase("Mar 5, 2024", "2024-03-05T00:00:00")]
        [TestCase(" 10:22:03 15/03/2024 ", "2024-03-15T10:22:03")]
        public void ParsesDatesAsUtc(string text, string expectedUtc)
        {
            var parsed = BunkrHtml.ParseDate(text);

            Assert.IsNotNull(parsed);
            Assert.AreEqual(DateTimeKind.Utc, parsed!.Value.Kind);
            Assert.AreEqual(DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), DateTime.SpecifyKind(parsed.Value, DateTimeKind.Unspecified));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("3 days ago")]
        [TestCase("31/02/2024")]
        public void UnparseableDatesAreNull(string? text)
        {
            Assert.IsNull(BunkrHtml.ParseDate(text));
        }

        [Test]
        public void PairsAlbumDatesWithItemsByPosition()
        {
            var html = @"<div class=""grid"">
<div class=""theItem""><a class=""after:absolute after:z-10 after:inset-0"" href=""/f/one""></a><span class=""theDate text-xs"">10:22:03 15/03/2024</span></div>
<div class=""theItem""><a class=""after:absolute after:z-10 after:inset-0"" href=""/f/two""></a><span class=""theDate"">not a date</span></div>
<div class=""theItem""><a class=""after:absolute after:z-10 after:inset-0"" href=""https://cdn.test/f/three""></a><p class=""theDate"">01/01/2023</p></div>
<div class=""theItem""><a class=""after:absolute after:z-10 after:inset-0"" href=""/f/four""></a></div>
</div>";

            var dates = BunkrHtml.GetItemDates(html, "https://bunkr.si");

            CollectionAssert.AreEquivalent(new[] { "https://bunkr.si/f/one", "https://cdn.test/f/three" }, dates.Keys);
            Assert.AreEqual(new DateTime(2024, 3, 15, 10, 22, 3, DateTimeKind.Utc), dates["https://bunkr.si/f/one"]);
            Assert.AreEqual(new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc), dates["https://cdn.test/f/three"]);
        }

        [Test]
        public void ReadsItemPageDateFromTimeTagOrRelativeDateTooltip()
        {
            Assert.AreEqual(new DateTime(2024, 3, 15, 10, 22, 3, DateTimeKind.Utc),
                BunkrHtml.GetItemDate(@"<p>Uploaded <time datetime=""2024-03-15T10:22:03Z"">March</time></p>"));
            Assert.AreEqual(new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                BunkrHtml.GetItemDate(@"<time>soon</time><img title=""2020-01-01""> today <span title=""Download"">x</span><span title=""2024-02-01"">2 weeks ago</span>"));
            Assert.IsNull(BunkrHtml.GetItemDate(@"<span title=""2024-02-01"">big file</span>"));
        }
    }

    public class BunkrResolverTests
    {
        private static string Item(string title, string cdn) => $@"<html><head>
<script>var jsCDN = ""{cdn.Replace("/", "\\/")}"";</script></head>
<body><h1 class=""text-subs font-semibold text-base sm:text-lg truncate"">{title}</h1></body></html>";

        private static string Album(string name, string items, int pages = 1) => $@"<html><body>
<div class=""text-subs font-semibold flex text-base sm:text-lg""><h1>{name}</h1></div>
{items}
{(pages > 1 ? "<nav class=\"pagination\">" + string.Join("", Enumerable.Range(1, pages).Select(p => $"<a>{p}</a>")) + "</nav>" : "")}
</body></html>";

        private static string Link(string href) => $@"<a class=""after:absolute after:z-10 after:inset-0"" href=""{href}""></a>";

        private static FakeHttp SigningApi(FakeHttp http) => http.On("GET", u => u.StartsWith(BunkrResolver.SignApi), r =>
        {
            var path = Uri.UnescapeDataString(r.Url.Substring(r.Url.IndexOf("path=", StringComparison.Ordinal) + 5));
            return new FileHostHttpResponse(200, "{\"token\":\"T" + path.Length + "\",\"ex\":1700000000}");
        });

        private static FileHostOptions Options() => new() { RetryBaseDelayMilliseconds = 0, MaxParallelRequests = 3 };

        [Test]
        public void ResolvesPaginatedAlbumInOrderWithSignedLinks()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/a/Alb1", 200, Album("My Album", Link("/f/a") + Link("/f/b"), pages: 2))
                .On("GET", "https://bunkr.si/a/Alb1?page=2", 200, Album("ignored", Link("/f/c") + Link("/f/b")))
                .On("GET", "https://bunkr.si/f/a", 200, Item("first.mp4", "https://s1.cdn.test/first-AAA.mp4"))
                .On("GET", "https://bunkr.si/f/b", 200, Item("second.jpg", "https://s2.cdn.test/second-BBB.jpg"))
                .On("GET", "https://bunkr.si/f/c", 200, Item("first.mp4", "https://s1.cdn.test/first-CCC.mp4"));

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/a/Alb1?page=5", null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { "first-AAA.mp4", "second-BBB.jpg", "first-CCC.mp4" }, files.Select(f => f.Name));
            Assert.IsTrue(files.All(f => f.RelativeFolder == "My Album"));
            Assert.AreEqual("https://s1.cdn.test/first-AAA.mp4?token=T14&ex=1700000000", files[0].Url);
            Assert.AreEqual("Bunkr", files[0].Host);
            CollectionAssert.AreEqual(new[] { BunkrResolver.DownloadReferer }, files[0].Headers["Referer"]);
            CollectionAssert.AreEqual(new[] { BunkrResolver.UserAgent }, files[0].Headers["User-Agent"]);

            var signed = http.RequestsTo(BunkrResolver.SignApi).Select(r => r.Url).OrderBy(u => u).ToList();
            CollectionAssert.Contains(signed, BunkrResolver.SignApi + "?path=%2Ffirst-AAA.mp4");
            Assert.AreEqual(1, http.RequestsTo("https://bunkr.si/f/b").Count(), "duplicate item across pages fetched once");
        }

        [Test]
        public void FallsBackToAlternateDomainOn403()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.fi/f/solo", 403, "blocked")
                .On("GET", "https://bunkr.cr/f/solo", 200, Item("solo.mp4", "https://s9.cdn.test/solo-Z.mp4"));
            var options = Options();
            options.CreateSubfolders = true;

            var files = new BunkrResolver(http, options).Resolve(new FileHostLink("bunkr.fi/f/solo", null), new FileHostContext());

            Assert.AreEqual("solo-Z.mp4", files.Single().Name);
            Assert.AreEqual("", files.Single().RelativeFolder, "single files are not put in a subfolder");
        }

        [Test]
        public void UsesDownloadApiWhenPageHasNoVariables()
        {
            var page = @"<html><head><script src=""x.js"" data-file-id=""4242""></script></head>
<body><h1 class=""text-subs font-semibold text-base sm:text-lg truncate"">archive.zip</h1></body></html>";
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/f/zipfile", 200, page)
                .On("POST", BunkrResolver.DownloadApi, 200, "{\"mediafiles\":\"https://m3.cdn.test/ignored\",\"path\":\"/archive-QQ.zip\"}");

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/f/zipfile", null), new FileHostContext());

            Assert.AreEqual("https://m3.cdn.test/archive-QQ.zip?token=T29&ex=1700000000", files.Single().Url);
            Assert.AreEqual("archive-QQ.zip", files.Single().Name);
            var post = http.RequestsTo(BunkrResolver.DownloadApi).Single();
            Assert.AreEqual("{\"id\":\"4242\"}", post.Body);
            Assert.AreEqual("application/json", post.Headers["Content-Type"]);
            StringAssert.EndsWith("path=%2Fstorage%2Fmedia%2Farchive-QQ.zip", http.RequestsTo(BunkrResolver.SignApi).Single().Url);
        }

        [Test]
        public void KeepsPlainCdnLinkWhenSigningReturnsNoToken()
        {
            var http = new FakeHttp()
                .On("GET", u => u.StartsWith(BunkrResolver.SignApi), _ => new FileHostHttpResponse(200, "{}"))
                .On("GET", "https://bunkr.si/v/vid", 200, Item("vid.mp4", "https://s1.cdn.test/vid-1.mp4"));

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/v/vid", null), new FileHostContext());

            Assert.AreEqual("https://s1.cdn.test/vid-1.mp4", files.Single().Url);
        }

        [Test]
        public void AppliesIncludeAndIgnoreFilters()
        {
            var items = Link("/f/1") + Link("/f/2") + Link("/f/3");
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/a/F", 200, Album("F", items))
                .On("GET", "https://bunkr.si/f/1", 200, Item("movie.mp4", "https://s.cdn.test/movie-1.mp4"))
                .On("GET", "https://bunkr.si/f/2", 200, Item("sample.mp4", "https://s.cdn.test/sample-2.mp4"))
                .On("GET", "https://bunkr.si/f/3", 200, Item("photo.jpg", "https://s.cdn.test/photo-3.jpg"));
            var options = Options();
            options.BunkrInclude = new[] { "mp4" };
            options.BunkrIgnore = new[] { "sample" };

            var files = new BunkrResolver(http, options).Resolve(new FileHostLink("https://bunkr.si/a/F", null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { "movie-1.mp4" }, files.Select(f => f.Name));
        }

        [Test]
        public void BrokenItemsAreSkippedAndReported()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/a/Br", 200, Album("Br", Link("/f/ok") + Link("/f/gone") + Link("/f/novars")))
                .On("GET", "https://bunkr.si/f/ok", 200, Item("ok.mp4", "https://s.cdn.test/ok-1.mp4"))
                .On("GET", "https://bunkr.si/f/novars", 200, "<html><h1 class=\"text-subs font-semibold text-base sm:text-lg truncate\">x.mp4</h1></html>");
            var errors = 0;
            var context = new FileHostContext((level, _) => { if (level == FileHostLogLevel.Error) System.Threading.Interlocked.Increment(ref errors); });

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/a/Br", null), context);

            CollectionAssert.AreEqual(new[] { "ok-1.mp4" }, files.Select(f => f.Name));
            Assert.AreEqual(2, errors);
        }

        [Test]
        public void AlbumDatesWinOverItemPageDates()
        {
            var items = @"<div>" + Link("/f/a") + @"<span class=""theDate"">08:00:00 02/01/2024</span></div>"
                + @"<div>" + Link("/f/b") + @"<span class=""theDate"">?</span></div>";
            var withTime = Item("b.mp4", "https://s.cdn.test/b-2.mp4").Replace("</body>", @"<time datetime=""2023-06-30"">x</time></body>");
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/a/D", 200, Album("D", items))
                .On("GET", "https://bunkr.si/f/a", 200, Item("a.mp4", "https://s.cdn.test/a-1.mp4").Replace("</body>", @"<time datetime=""2000-01-01"">x</time></body>"))
                .On("GET", "https://bunkr.si/f/b", 200, withTime);

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/a/D", null), new FileHostContext());

            Assert.AreEqual(new DateTime(2024, 1, 2, 8, 0, 0, DateTimeKind.Utc), files[0].LastModified);
            Assert.AreEqual(new DateTime(2023, 6, 30, 0, 0, 0, DateTimeKind.Utc), files[1].LastModified);
        }

        [Test]
        public void SingleFileWithoutDateKeepsNoDate()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/f/nodate", 200, Item("n.mp4", "https://s.cdn.test/n-1.mp4"));

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/f/nodate", null), new FileHostContext());

            Assert.IsNull(files.Single().LastModified);
        }

        [Test]
        public void CleanNameKeepsThePageTitleAndNumbersRepeats()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", "https://bunkr.si/a/C", 200, Album("C", Link("/f/1") + Link("/f/2") + Link("/f/3")))
                .On("GET", "https://bunkr.si/f/1", 200, Item("holiday.mp4", "https://s.cdn.test/holiday-AAA.mp4"))
                .On("GET", "https://bunkr.si/f/2", 200, Item("holiday.mp4", "https://s.cdn.test/holiday-BBB.mp4"))
                .On("GET", "https://bunkr.si/f/3", 200, "<html><head><script>var jsCDN = \"https:\\/\\/s.cdn.test\\/untitled-CCC.jpg\";</script></head></html>");
            var options = Options();
            options.BunkrCleanName = true;

            var files = new BunkrResolver(http, options).Resolve(new FileHostLink("https://bunkr.si/a/C", null), new FileHostContext());

            CollectionAssert.AreEqual(new[] { "holiday.mp4", "holiday(1).mp4", "untitled-CCC.jpg" }, files.Select(f => f.Name));
            Assert.AreEqual("https://s.cdn.test/holiday-BBB.mp4?token=T16&ex=1700000000", files[1].Url);
        }

        [Test]
        public void DownloadsUseTheCurrentReferer()
        {
            Assert.AreEqual("https://dl.bunkrr.cr/", BunkrResolver.DownloadReferer);
        }

        [Test]
        public void EmptyAlbumIsAnError()
        {
            var http = new FakeHttp().On("GET", "https://bunkr.si/a/Empty", 200, Album("Empty", ""));

            Assert.Throws<FileHostException>(() =>
                new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/a/Empty", null), new FileHostContext()));
        }

        [Test]
        public void WarnsWhenServerIsOffline()
        {
            var http = SigningApi(new FakeHttp())
                .On("GET", BunkrResolver.StatusPage, 200,
                    @"<div class=""flex items-center gap-4 py-4 border-b border-soft""><p>s1</p><span>Non-operational</span></div>")
                .On("GET", "https://bunkr.si/f/a", 200, Item("a.mp4", "https://s1.cdn.test/a-1.mp4"));
            var warnings = 0;
            var context = new FileHostContext((level, _) => { if (level == FileHostLogLevel.Warning) warnings++; });

            var files = new BunkrResolver(http, Options()).Resolve(new FileHostLink("https://bunkr.si/f/a", null), context);

            Assert.AreEqual(1, files.Count);
            Assert.AreEqual(1, warnings);
        }
    }
}
