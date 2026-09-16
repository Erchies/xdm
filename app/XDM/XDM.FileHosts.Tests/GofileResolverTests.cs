using System.IO;
using System.Linq;
using NUnit.Framework;
using XDM.Core.FileHosts;

namespace XDM.FileHosts.Tests
{
    public class GofileResolverTests
    {
        private const long Now = 123456L * 14400 + 7;

        // Reference values computed with hashlib.sha256 in the Python gofile_core.
        [Test]
        public void WebsiteTokenMatchesPythonImplementation()
        {
            Assert.AreEqual("0159fb106119c9b0af9a9a559e06b751804d682968f27385c8b0045b3fbeed96",
                GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "abc", "12af056dacea0b", Now));
            Assert.AreEqual("3484cf6c1475a9494d157c405663687d543d6492dc4352f2057673d526be7e6a",
                GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "", "saltX", Now, slotOffset: -1));
        }

        [Test]
        public void PasswordHashMatchesPythonImplementation()
        {
            Assert.AreEqual("afdd5ee7b28f2221159badc5643ec03361d0fb5f54dfe7003ffac023742a2fa9",
                GofileResolver.Sha256Hex("senha-çã"));
        }

        [TestCase("https://gofile.io/d/AbC123", "AbC123")]
        [TestCase("https://gofile.io/d/AbC123/?utm=x#frag", "AbC123")]
        [TestCase("gofile.io/w/xyz9", "xyz9")]
        [TestCase("  abcd12  ", "abcd12")]
        public void ParsesContentIds(string url, string expected)
        {
            Assert.AreEqual(expected, GofileResolver.ParseContentId(url));
        }

        [TestCase("")]
        [TestCase("https://example.com/d/abc")]
        [TestCase("ab")]
        public void RejectsInvalidLinks(string url)
        {
            Assert.Throws<FileHostException>(() => GofileResolver.ParseContentId(url));
        }

        private static FakeHttp GuestAccount(FakeHttp http) =>
            http.On("POST", GofileResolver.ApiBase + "/accounts", 200, "{\"status\":\"ok\",\"data\":{\"token\":\"tok\"}}");

        private static GofileResolver CreateResolver(FakeHttp http, FileHostOptions? options = null) =>
            new(http, options ?? new FileHostOptions { RetryBaseDelayMilliseconds = 0 }, () => Now);

        [Test]
        public void WalksFoldersKeepingStructureAndHeaders()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/root1", 200, @"{
                    ""status"": ""ok"",
                    ""data"": {
                        ""type"": ""folder"", ""name"": ""ignored root name"", ""passwordStatus"": ""passwordOk"",
                        ""children"": {
                            ""c1"": { ""type"": ""file"", ""name"": ""video.mp4"", ""size"": 1048576, ""link"": ""https://store1.gofile.io/download/web/c1/video.mp4"" },
                            ""c2"": { ""type"": ""folder"", ""id"": ""sub1"", ""name"": ""Sub"" },
                            ""c3"": { ""type"": ""file"", ""name"": ""VIDEO.mp4"", ""link"": ""https://store1.gofile.io/download/web/c3/VIDEO.mp4"" },
                            ""c4"": { ""type"": ""file"", ""name"": ""no-link.bin"" },
                            ""c5"": { ""type"": ""file"", ""name"": ""../../escape.bat"", ""size"": 12.0, ""link"": ""https://store1.gofile.io/download/web/c5/x"" }
                        }
                    }
                }")
                .On("GET", GofileResolver.ApiBase + "/contents/sub1", 200, @"{
                    ""status"": ""ok"",
                    ""data"": { ""type"": ""folder"", ""name"": ""Sub"", ""children"": {
                        ""d1"": { ""type"": ""file"", ""name"": ""a.txt"", ""size"": 3, ""link"": ""https://store2.gofile.io/download/web/d1/a.txt"" }
                    } }
                }");

            var warnings = 0;
            var context = new FileHostContext((level, _) => { if (level == FileHostLogLevel.Warning) warnings++; });
            var files = CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/root1", null), context);

            CollectionAssert.AreEqual(new[] { "video.mp4", "a.txt", "VIDEO(1).mp4", "escape.bat" }, files.Select(f => f.Name));
            Assert.AreEqual("root1", files[0].RelativeFolder);
            Assert.AreEqual(Path.Combine("root1", "Sub"), files[1].RelativeFolder);
            Assert.AreEqual(1048576, files[0].Size);
            Assert.AreEqual(-1, files[2].Size);
            Assert.AreEqual(12, files[3].Size);
            Assert.AreEqual(1, warnings, "file without link is reported");

            var file = files[0];
            Assert.AreEqual("GoFile", file.Host);
            Assert.AreEqual("https://gofile.io/d/root1", file.SourceUrl);
            Assert.AreEqual("accountToken=tok", file.Cookies);
            CollectionAssert.AreEqual(new[] { "Bearer tok" }, file.Headers["Authorization"]);
            CollectionAssert.AreEqual(new[] { "Mozilla/5.0" }, file.Headers["User-Agent"]);

            var listing = http.RequestsTo(GofileResolver.ApiBase + "/contents/root1").Single();
            StringAssert.Contains("cache=true", listing.Url);
            StringAssert.DoesNotContain("password=", listing.Url);
            Assert.AreEqual("Bearer tok", listing.Headers["Authorization"]);
            Assert.AreEqual("accountToken=tok", listing.Headers["Cookie"]);
            Assert.AreEqual(GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "tok", GofileResolver.DefaultWebsiteTokenSalt, Now),
                listing.Headers["X-Website-Token"]);

            var account = http.RequestsTo(GofileResolver.ApiBase + "/accounts").Single();
            Assert.IsFalse(account.Headers.ContainsKey("Authorization"));
            Assert.AreEqual(GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "", GofileResolver.DefaultWebsiteTokenSalt, Now),
                account.Headers["X-Website-Token"]);
        }

        [Test]
        public void SingleFileLinkAndNoSubfolder()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/file1", 200,
                    "{\"status\":\"ok\",\"data\":{\"type\":\"file\",\"name\":\"doc.pdf\",\"size\":5,\"link\":\"https://s.gofile.io/doc.pdf\"}}");
            var options = new FileHostOptions { CreateSubfolders = false, RetryBaseDelayMilliseconds = 0 };

            var files = CreateResolver(http, options).Resolve(new FileHostLink("https://gofile.io/d/file1", null), new FileHostContext());

            Assert.AreEqual("doc.pdf", files.Single().Name);
            Assert.AreEqual("", files.Single().RelativeFolder);
        }

        [Test]
        public void SendsHashedPasswordAndConfiguredTokenAndSalt()
        {
            var http = new FakeHttp()
                .On("GET", GofileResolver.ApiBase + "/contents/locked", 200,
                    "{\"status\":\"ok\",\"data\":{\"type\":\"folder\",\"children\":{}}}");
            var options = new FileHostOptions { GofileAccountToken = " mytoken ", GofileWebsiteTokenSalt = "newsalt", RetryBaseDelayMilliseconds = 0 };

            CreateResolver(http, options).Resolve(new FileHostLink("https://gofile.io/d/locked", "senha-çã"), new FileHostContext());

            Assert.IsEmpty(http.RequestsTo(GofileResolver.ApiBase + "/accounts"), "configured token skips the guest account");
            var request = http.Requests.Single();
            StringAssert.Contains("password=afdd5ee7b28f2221159badc5643ec03361d0fb5f54dfe7003ffac023742a2fa9", request.Url);
            Assert.AreEqual("Bearer mytoken", request.Headers["Authorization"]);
            Assert.AreEqual(GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "mytoken", "newsalt", Now), request.Headers["X-Website-Token"]);
        }

        [TestCase("error-passwordRequired")]
        [TestCase("error-passwordWrong")]
        public void PasswordErrors(string status)
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/", 200, "{\"status\":\"" + status + "\"}");

            Assert.Throws<FileHostPasswordException>(() =>
                CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/abcd", null), new FileHostContext()));
        }

        [Test]
        public void PasswordStatusInsideDataIsAnError()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/", 200,
                    "{\"status\":\"ok\",\"data\":{\"type\":\"folder\",\"passwordStatus\":\"passwordRequired\"}}");

            Assert.Throws<FileHostPasswordException>(() =>
                CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/abcd", null), new FileHostContext()));
        }

        [Test]
        public void RefusedListingRetriesPreviousTokenSlotThenFails()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/", 401, "{\"status\":\"error-notPremium\"}");

            var ex = Assert.Throws<FileHostException>(() =>
                CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/abcd", null), new FileHostContext()));

            Assert.IsNotInstanceOf<FileHostPasswordException>(ex);
            var tokens = http.RequestsTo(GofileResolver.ApiBase + "/contents/").Select(r => r.Headers["X-Website-Token"]).ToList();
            CollectionAssert.AreEqual(new[]
            {
                GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "tok", GofileResolver.DefaultWebsiteTokenSalt, Now, 0),
                GofileResolver.GenerateWebsiteToken("Mozilla/5.0", "tok", GofileResolver.DefaultWebsiteTokenSalt, Now, -1)
            }, tokens);
        }

        [Test]
        public void NotFoundIsReported()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/", 404, "{\"status\":\"error-notFound\"}");

            Assert.Throws<FileHostException>(() =>
                CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/abcd", null), new FileHostContext()));
            Assert.AreEqual(1, http.RequestsTo(GofileResolver.ApiBase + "/contents/").Count());
        }

        [Test]
        public void HtmlInsteadOfJsonIsAnError()
        {
            var http = new FakeHttp().On("POST", GofileResolver.ApiBase + "/accounts", 403, "<html>Cloudflare</html>");

            var ex = Assert.Throws<FileHostException>(() =>
                CreateResolver(http).Resolve(new FileHostLink("https://gofile.io/d/abcd", null), new FileHostContext()));
            StringAssert.Contains("403", ex!.Message);
        }

        [Test]
        public void GuestAccountIsCreatedOncePerResolver()
        {
            var http = GuestAccount(new FakeHttp())
                .On("GET", GofileResolver.ApiBase + "/contents/", 200, "{\"status\":\"ok\",\"data\":{\"type\":\"folder\",\"children\":{}}}");
            var resolver = CreateResolver(http);

            resolver.Resolve(new FileHostLink("https://gofile.io/d/aaaa", null), new FileHostContext());
            resolver.Resolve(new FileHostLink("https://gofile.io/d/bbbb", null), new FileHostContext());

            Assert.AreEqual(1, http.RequestsTo(GofileResolver.ApiBase + "/accounts").Count());
        }
    }
}
