using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpokysProjectVercel.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SpokysProjectVercel.Services
{
    public static class GameScraperService
    {
        private static readonly HttpClient Client = new();
        private static readonly string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static string AbsUrl(string baseUrl, string href)
        {
            if (href.StartsWith("http")) return href;
            if (href.StartsWith("//")) return "https:" + href;
            var root = baseUrl.TrimEnd('/');
            return root + (href.StartsWith("/") ? "" : "/") + href;
        }

        private static string NormalizeName(string name)
        {
            return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9 ]", "").Trim();
        }

        private static string[] ImportantWords(string query)
        {
            var words = Regex.Split(query.ToLowerInvariant(), @"\s+")
                .Where(w => w.Length > 2 && !new[] { "the", "and", "for", "with", "from", "edition", "ultimate", "deluxe", "game", "of", "year" }.Contains(w))
                .ToArray();
            return words.Length > 0 ? words : query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private static int ScoreTitle(string query, string title)
        {
            var qWords = ImportantWords(query);
            if (qWords.Length == 0) return 0;
            var titleNorm = NormalizeName(title);
            var queryNorm = NormalizeName(query);
            if (titleNorm == queryNorm) return 100;
            if (!qWords.All(w => titleNorm.Contains(w))) return 0;
            return 50 + qWords.Count(w => titleNorm.Contains(w)) * 10;
        }

        public static async Task<List<ScrapedGame>> SearchSteamRip(string query, int limit = 15)
        {
            var q = Regex.Replace(query, "[™®©]", "").Trim();
            if (string.IsNullOrEmpty(q)) return new();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://steamrip.com/?s={Uri.EscapeDataString(q)}");
                req.Headers.Add("User-Agent", UA);
                req.Headers.Add("Accept", "text/html");
                var res = await Client.SendAsync(req);
                var html = await res.Content.ReadAsStringAsync();
                if (html.Contains("Just a moment")) return new();

                var results = new List<ScrapedGame>();
                var seen = new HashSet<string>();
                var re = new Regex(@"<a[^>]*href=""([^""]+/)"".*?>([^<]*Free Download[^<]*)</a>", RegexOptions.IgnoreCase);
                foreach (Match m in re.Matches(html))
                {
                    var href = m.Groups[1].Value;
                    var name = Regex.Replace(m.Groups[2].Value, @"\s+", " ").Trim();
                    if (string.IsNullOrEmpty(name) || href.Contains("/category/") || href.Contains("/tag/")) continue;
                    var pageUrl = AbsUrl("https://steamrip.com", href);
                    if (seen.Contains(pageUrl)) continue;
                    seen.Add(pageUrl);
                    name = Regex.Replace(name, @"\s*Free Download.*$", "", RegexOptions.IgnoreCase).Trim();
                    results.Add(new ScrapedGame { Name = string.IsNullOrEmpty(name) ? m.Groups[2].Value.Trim() : name, PageUrl = pageUrl, Source = "steamrip" });
                }
                return results.Where(r => ScoreTitle(q, r.Name) > 0).OrderByDescending(r => ScoreTitle(q, r.Name)).Take(limit).ToList();
            }
            catch { return new(); }
        }

        public static async Task<List<ScrapedGame>> SearchDodi(string query, int limit = 15)
        {
            var q = Regex.Replace(query, "[™®©]", "").Trim();
            if (string.IsNullOrEmpty(q)) return new();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://dodi-repacks.download/?s={Uri.EscapeDataString(q)}");
                req.Headers.Add("User-Agent", UA);
                req.Headers.Add("Accept", "text/html");
                var res = await Client.SendAsync(req);
                var html = await res.Content.ReadAsStringAsync();
                if (html.Contains("Just a moment")) return new();

                var results = new List<ScrapedGame>();
                var seen = new HashSet<string>();
                var patterns = new[]
                {
                    new Regex(@"<h2[^>]*class=""[^""]*entry-title[^""]*"".*?>\s*<a[^>]*href=""([^""]+)"".*?>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
                    new Regex(@"<a[^>]*href=""(https://dodi-repacks\.download/[^""]+)""[^>]*rel=""bookmark""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                };
                foreach (var re in patterns)
                {
                    foreach (Match m in re.Matches(html))
                    {
                        var href = m.Groups[1].Value;
                        var name = Regex.Replace(m.Groups[2].Value, "<[^>]*>", "").Replace("&#8211;", "-").Replace("&#8217;", "'").Replace("&amp;", "&").Trim();
                        if (string.IsNullOrEmpty(name) || href.Contains("/category/") || href.Contains("/tag/") || href.Contains("/page/")) continue;
                        var pageUrl = AbsUrl("https://dodi-repacks.download", href);
                        if (seen.Contains(pageUrl)) continue;
                        seen.Add(pageUrl);
                        results.Add(new ScrapedGame { Name = name, PageUrl = pageUrl, Source = "dodi" });
                        if (results.Count >= limit) break;
                    }
                    if (results.Count >= limit) break;
                }
                return results;
            }
            catch { return new(); }
        }

        public static async Task<List<ScrapedGame>> SearchAll(string query, int limit = 25)
        {
            var (steamrip, dodi) = (await SearchSteamRip(query, 15), await SearchDodi(query, 15));
            var merged = new List<ScrapedGame>();
            var seen = new HashSet<string>();
            foreach (var r in steamrip.Concat(dodi))
            {
                var key = NormalizeName(r.Name);
                if (seen.Contains(key)) continue;
                seen.Add(key);
                merged.Add(r);
                if (merged.Count >= limit) break;
            }
            return merged.Take(limit).ToList();
        }

        public static async Task<ScrapedGame> ScrapePage(ScrapedGame game)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, game.PageUrl);
                req.Headers.Add("User-Agent", UA);
                req.Headers.Add("Accept", "text/html");
                var res = await Client.SendAsync(req);
                var html = await res.Content.ReadAsStringAsync();

                var downloads = new List<DownloadLink>();
                var seen = new HashSet<string>();
                var patterns = new[]
                {
                    new Regex(@"href=""([^""]*\?ddownload=[^""]*)""", RegexOptions.IgnoreCase),
                    new Regex(@"href=""([^""]*\?tdownload=[^""]*)""", RegexOptions.IgnoreCase),
                    new Regex(@"<a[^>]*href=""([^""]+)""[^>]*class=""[^""]*shortc-button[^""]*"".*?</a>", RegexOptions.IgnoreCase),
                    new Regex(@"<a[^>]*href=""([^""]+)""[^>]*class=""[^""]*wp-block-button__link[^""]*"".*?</a>", RegexOptions.IgnoreCase),
                    new Regex(@"<a[^>]*href=""(https?://[^""]+)""[^>]*>(?:Download|DOWNLOAD|Torrent|Magnet).*?</a>", RegexOptions.IgnoreCase)
                };
                var dlCount = 1;
                foreach (var re in patterns)
                {
                    foreach (Match m in re.Matches(html))
                    {
                        var url = AbsUrl(game.PageUrl, m.Groups[1].Value);
                        if (url.Contains("steamrip.com") || url.Contains("online-fix.me") || seen.Contains(url)) continue;
                        if (url.Contains("dodi-repacks.download") && !url.Contains("?ddownload") && !url.Contains("?tdownload")) continue;
                        seen.Add(url);
                        downloads.Add(new DownloadLink { Name = $"Download Link {dlCount++}", Url = url, Label = $"Download Link {dlCount - 1}" });
                    }
                }
                return new ScrapedGame { Name = game.Name, PageUrl = game.PageUrl, Source = game.Source, Downloads = downloads };
            }
            catch { return game; }
        }

        public static async Task<string?> DownloadFileAsync(string url, string destDir, IProgress<double>? progress = null)
        {
            try
            {
                progress?.Report(0);
                var fileName = Path.GetFileName(new Uri(url).AbsolutePath) ?? "game_download";
                if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".zip";
                var filePath = Path.Combine(destDir, SanitizeName(fileName));

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", UA);
                using var res = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                res.EnsureSuccessStatusCode();

                var total = res.Content.Headers.ContentLength ?? -1;
                var totalRead = 0L;
                await using var stream = await res.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                var buffer = new byte[65536];

                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    await fs.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (total > 0) progress?.Report((double)totalRead / total * 100);
                }

                progress?.Report(100);
                return filePath;
            }
            catch { return null; }
        }

        public static Task<bool> ExtractArchiveAsync(string archive, string destDir, IProgress<double>? progress = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    progress?.Report(0);
                    using var arc = ArchiveFactory.OpenArchive(archive);
                    var entries = arc.Entries.Where(e => !e.IsDirectory).ToList();
                    var processed = 0;

                    foreach (var entry in entries)
                    {
                        entry.WriteToDirectory(destDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                        processed++;
                        progress?.Report((double)processed / entries.Count * 100);
                    }

                    progress?.Report(100);
                    return Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Length > 0;
                }
                catch { return false; }
            });
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
