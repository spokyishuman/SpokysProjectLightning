using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    /// <summary>
    /// Online-Fix.me scraper — ported from the proven Discord bot (onlinefix.ts).
    /// Handles: index building/caching, fuzzy game lookup, DLE pagination,
    /// the 4 real download endpoints (hosters/drive/uploads/torrent),
    /// password/version/filesize extraction, and cookie warming (401 fix).
    /// </summary>
    public class OnlineFixScraperService
    {
        private const string Base = "https://online-fix.me";
        private const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        public static readonly string[] Categories =
        {
            "officialservers", "vr", "survival", "adventures", "horror", "action",
            "racing", "rpg", "shooter", "simulator", "strategy", "fighting",
            "sandbox", "arcade", "puzzles"
        };

        private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "and", "or", "for", "in", "on", "at", "to", "with",
            "game", "games", "online", "edition", "definitive", "deluxe", "remastered",
            "remaster", "hd", "goty", "by", "po", "seti", "multiplayer", "coop", "co-op"
        };

        // href="/games/{category}/{id}-{slug}.html" with optional title attr
        private static readonly Regex GameLinkRe = new(
            @"href=""(?:https://online-fix\.me)?(/games/([^/]+)/(\d+)-([^""]+)\.html)""[^>]*(?:title=""([^""]*)"")?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly CookieContainer _cookies = new();
        private readonly SemaphoreSlim _gate = new(4, 4); // polite concurrency

        // Persistent index
        private static readonly string IndexDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpokysPL");
        private static readonly string IndexPath = Path.Combine(IndexDir, "of-index.json");
        private static readonly string CachePath = Path.Combine(IndexDir, "of-cache.json");

        private OnlineFixIndexFile? _index;
        private bool _indexBuilding;
        private readonly ConcurrentDictionary<string, string> _memoryCache = new();
        private string _cachedCookieHeader = "";

        public static event EventHandler<string>? Log;
        private static void LogMsg(string msg) => Log?.Invoke(null, msg);

        public OnlineFixScraperService()
        {
            Directory.CreateDirectory(IndexDir);
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent", UA);
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _http.Timeout = TimeSpan.FromSeconds(20);
            LoadIndexFromDisk();
        }

        // ========== HELPERS (ported from onlinefix.ts) ==========

        public static string NormalizeName(string input)
        {
            var s = input.ToLowerInvariant().Normalize(NormalizationForm.FormKD);
            // strip combining marks (accents)
            var sb = new StringBuilder();
            foreach (var ch in s.Where(c => c < 0x0300 || c > 0x036F))
                sb.Append(ch);
            s = sb.ToString();
            s = Regex.Replace(s, @"[^\w\s-]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        public static List<string> ImportantWords(string input)
            => NormalizeName(input).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 1 && !FillerWords.Contains(w)).ToList();

        public static string Slugify(string input)
            => Regex.Replace(NormalizeName(input), @"\s+", "-").Trim('-');

        public static int ScoreMatch(string query, string title, string slug)
        {
            var qWords = ImportantWords(query);
            if (qWords.Count == 0) return 0;

            var titleNorm = NormalizeName(title);
            var slugNorm = NormalizeName(slug.Replace('-', ' '));
            var queryNorm = NormalizeName(query);
            var allTokens = ImportantWords(title).Concat(ImportantWords(slug.Replace('-', ' '))).ToList();

            bool AllMatch() => qWords.All(w =>
                allTokens.Any(t => t == w || t.Contains(w) || w.Contains(t))
                || titleNorm.Contains(w) || slugNorm.Contains(w));

            if (!AllMatch()) return 0;

            if (titleNorm == queryNorm) return 100;
            var slugTrimmed = Regex.Replace(slug, @"-po-seti.*$", "", RegexOptions.IgnoreCase);
            slugTrimmed = Regex.Replace(slugTrimmed, @"-online.*$", "", RegexOptions.IgnoreCase);
            if (Slugify(query) == slugTrimmed) return 90;
            if (qWords.All(w => allTokens.Any(t => t == w || t.Contains(w)))) return 80;
            return 50;
        }

        // ========== HTTP ==========

        private Dictionary<string, string> Headers(Dictionary<string, string>? extra = null)
        {
            var h = new Dictionary<string, string>
            {
                ["User-Agent"] = UA,
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Referer"] = Base
            };
            if (!string.IsNullOrEmpty(_cachedCookieHeader))
                h["Cookie"] = _cachedCookieHeader;
            if (extra != null)
                foreach (var kv in extra) h[kv.Key] = kv.Value;
            return h;
        }

        private async Task<string> RequestAsync(string url, HttpMethod? method = null,
            Dictionary<string, string>? extraHeaders = null, string? body = null, int retries = 3)
        {
            await _gate.WaitAsync();
            try
            {
                for (int attempt = 1; attempt <= retries; attempt++)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(method ?? HttpMethod.Get, url);
                        foreach (var kv in Headers(extraHeaders))
                            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        if (body != null)
                            req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
                        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        // capture set-cookie for cookie warming
                        if (res.Headers.Contains("Set-Cookie"))
                        {
                            var sc = res.Headers.GetValues("Set-Cookie").ToList();
                            if (sc.Count > 0) _cachedCookieHeader = string.Join("; ", sc.Select(CookieNameValue));
                        }
                        if ((int)res.StatusCode >= 500) throw new Exception($"HTTP {(int)res.StatusCode}");
                        return await res.Content.ReadAsStringAsync();
                    }
                    catch (Exception)
                    {
                        if (attempt == retries) throw;
                        await Task.Delay(800 * attempt);
                    }
                }
                throw new Exception("Request failed");
            }
            finally { _gate.Release(); }
        }

        private static string CookieNameValue(string setCookie)
        {
            // "name=value; Path=/; ..." -> "name=value"
            var parts = setCookie.Split(';');
            return parts.Length > 0 ? parts[0].Trim() : setCookie;
        }

        /// <summary>Visit homepage first so cookies are set (avoids 401 "User not recognized").</summary>
        public async Task WarmCookiesAsync()
        {
            try { await RequestAsync(Base); }
            catch { /* ignore */ }
        }

        // ========== INDEX ==========

        public class IndexGame
        {
            [JsonProperty("url")] public string Url { get; set; } = "";
            [JsonProperty("path")] public string Path { get; set; } = "";
            [JsonProperty("title")] public string Title { get; set; } = "";
            [JsonProperty("slug")] public string Slug { get; set; } = "";
            [JsonProperty("category")] public string Category { get; set; } = "";
            [JsonProperty("image")] public string Image { get; set; } = "";
        }

        public class OnlineFixIndexFile
        {
            [JsonProperty("builtAt")] public string BuiltAt { get; set; } = "";
            [JsonProperty("gameCount")] public int GameCount { get; set; }
            [JsonProperty("entries")] public Dictionary<string, string> Entries { get; set; } = new();
            [JsonProperty("catalog")] public List<IndexGame> Catalog { get; set; } = new();
        }

        public OnlineFixIndexFile? LoadIndexFromDisk()
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    _index = JsonConvert.DeserializeObject<OnlineFixIndexFile>(File.ReadAllText(IndexPath));
                    LogMsg($"[INDEX] loaded {_index?.GameCount ?? 0} games");
                    return _index;
                }
            }
            catch { }
            return null;
        }

        private void SaveIndex(List<IndexGame> catalog)
        {
            var entries = new Dictionary<string, string>();
            foreach (var g in catalog)
            {
                var keys = new HashSet<string>
                {
                    NormalizeName(!string.IsNullOrEmpty(g.Title) ? g.Title : g.Slug.Replace('-', ' ')),
                    NormalizeName(Regex.Replace(g.Slug, @"-po-seti.*$", "", RegexOptions.IgnoreCase)
                        .Replace('-', ' '))
                };
                foreach (var k in keys.Where(k => k.Length > 1))
                    entries[k] = g.Url;
            }
            _index = new OnlineFixIndexFile
            {
                BuiltAt = DateTime.UtcNow.ToString("o"),
                GameCount = catalog.Count,
                Entries = entries,
                Catalog = catalog
            };
            Directory.CreateDirectory(IndexDir);
            File.WriteAllText(IndexPath, JsonConvert.SerializeObject(_index, Formatting.Indented));
        }

        private List<IndexGame> ParseGames(string html)
        {
            var games = new List<IndexGame>();
            var seen = new HashSet<string>();
            foreach (Match m in GameLinkRe.Matches(html))
            {
                var p = m.Groups[1].Value;
                if (!seen.Add(p)) continue;
                var category = m.Groups[2].Value;
                var slug = m.Groups[4].Value;
                var rawTitle = m.Groups[5].Success ? m.Groups[5].Value : "";
                // strip Russian "по сети" trailing
                rawTitle = Regex.Replace(rawTitle, @"\s*по\s*сети.*", "", RegexOptions.IgnoreCase).Trim();
                games.Add(new IndexGame
                {
                    Url = Base + p,
                    Path = p,
                    Category = category,
                    Slug = slug,
                    Title = rawTitle
                });
            }
            return games;
        }

        private async Task<List<IndexGame>> CrawlListingAsync(string listUrl, string label)
        {
            await WarmCookiesAsync();
            var found = new Dictionary<string, IndexGame>();
            var first = await RequestAsync(listUrl);
            // Attach poster images from data-src attrs
            var withImages = AttachImages(first, ParseGames(first));
            foreach (var g in withImages) found[g.Path] = g;

            var postHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["X-Requested-With"] = "XMLHttpRequest",
                ["Referer"] = listUrl
            };
            for (int page = 1; page <= 500; page++)
            {
                string json;
                try
                {
                    json = await RequestAsync(listUrl, HttpMethod.Post, postHeaders, $"show_more={page}");
                }
                catch { break; }

                JObject? data;
                try { data = JObject.Parse(json); } catch { break; }
                var content = data?["content"]?.Value<string>();
                if (string.IsNullOrEmpty(content)) break;

                var before = found.Count;
                foreach (var g in AttachImages(content, ParseGames(content)))
                    found[g.Path] = g;
                if (found.Count == before) break; // no more pages
            }
            return found.Values.ToList();
        }

        private List<IndexGame> AttachImages(string html, List<IndexGame> games)
        {
            // Match data-src="https://online-fix.me/uploads/posts/.../poster.jpg" alt="Title"
            var imgRe = new Regex(
                @"data-src=""(https://online-fix\.me/uploads/posts/[^""]+\.jpg)""[^>]*alt=""([^""]*)""",
                RegexOptions.IgnoreCase);
            var imgByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in imgRe.Matches(html))
                imgByTitle[NormalizeName(m.Groups[2].Value)] = m.Groups[1].Value;

            foreach (var g in games)
            {
                if (!string.IsNullOrEmpty(g.Image)) continue;
                if (!string.IsNullOrEmpty(g.Title) && imgByTitle.TryGetValue(NormalizeName(g.Title), out var img))
                    g.Image = img;
            }
            return games;
        }

        public async Task<OnlineFixIndexFile> BuildIndexAsync(Action<string>? onProgress = null)
        {
            if (_indexBuilding) throw new Exception("Already building");
            _indexBuilding = true;
            try
            {
                var all = new Dictionary<string, IndexGame>();
                void Progress(string m) { LogMsg(m); onProgress?.Invoke(m); }

                Progress("Crawling main listing...");
                var main = await CrawlListingAsync($"{Base}/games/", "main");
                foreach (var g in main) all[g.Path] = g;

                if (main.Count < 50)
                {
                    foreach (var cat in Categories)
                    {
                        Progress($"Crawling {cat}...");
                        try
                        {
                            var games = await CrawlListingAsync($"{Base}/games/{cat}/", cat);
                            foreach (var g in games) all[g.Path] = g;
                        }
                        catch { /* skip failed category */ }
                    }
                }
                SaveIndex(all.Values.ToList());
                Progress($"Index built: {all.Count} games");
                return _index!;
            }
            finally { _indexBuilding = false; }
        }

        private List<(IndexGame game, int score)> RankCandidates(string query, List<IndexGame> games)
            => games
                .Select(g => (game: g, score: ScoreMatch(query,
                    !string.IsNullOrEmpty(g.Title) ? g.Title : g.Slug.Replace('-', ' '), g.Slug)))
                .Where(r => r.score > 0)
                .OrderByDescending(r => r.score)
                .ToList();

        private (string url, int score)? SearchIndex(string query)
        {
            var key = NormalizeName(query);
            if (_index?.Entries.TryGetValue(key, out var url) == true && !string.IsNullOrEmpty(url))
                return (url, 100);
            if (_index?.Catalog == null || _index.Catalog.Count == 0) return null;
            var ranked = RankCandidates(query, _index.Catalog);
            return ranked.Count > 0 ? (ranked[0].game.Url, ranked[0].score) : null;
        }

        private async Task<List<IndexGame>> SearchLiveAsync(string query)
        {
            var urls = new[]
            {
                $"{Base}/?s={Uri.EscapeDataString(query)}",
                $"{Base}/index.php?do=search&subaction=search&story={Uri.EscapeDataString(query)}"
            };
            var found = new Dictionary<string, IndexGame>();
            foreach (var u in urls)
            {
                try
                {
                    var html = await RequestAsync(u);
                    foreach (var g in AttachImages(html, ParseGames(html))) found[g.Path] = g;
                }
                catch { }
            }
            return found.Values.ToList();
        }

        /// <summary>
        /// Find the game page URL for a query (game name or Steam App ID).
        /// Cache → index → live search → rebuild fallback.
        /// </summary>
        public async Task<string?> FindGamePageAsync(string query, bool refreshIndex = false)
        {
            var key = NormalizeName(query);
            if (_memoryCache.TryGetValue(key, out var cached)) return cached;

            // If pure number, try App ID search first
            if (Regex.IsMatch(query.Trim(), @"^\d+$"))
            {
                var byId = await SearchByAppIdAsync(query.Trim());
                if (byId != null) { _memoryCache[key] = byId; return byId; }
            }

            var indexed = SearchIndex(query);
            if (indexed != null) { _memoryCache[key] = indexed.Value.url; return indexed.Value.url; }

            var live = await SearchLiveAsync(query);
            var ranked = RankCandidates(query, live);
            if (ranked.Count > 0) { _memoryCache[key] = ranked[0].game.Url; return ranked[0].game.Url; }

            if (_index?.Catalog == null || _index.Catalog.Count == 0 || refreshIndex)
            {
                await BuildIndexAsync();
                indexed = SearchIndex(query);
                if (indexed != null) { _memoryCache[key] = indexed.Value.url; return indexed.Value.url; }
            }
            return null;
        }

        private async Task<string?> SearchByAppIdAsync(string appId)
        {
            try
            {
                var html = await RequestAsync($"{Base}/index.php?do=search&subaction=search&story={Uri.EscapeDataString(appId)}");
                var g = ParseGames(html);
                return g.FirstOrDefault()?.Url;
            }
            catch { return null; }
        }

        // ========== DOWNLOAD LINK PARSING (ported from parseDownloadLinks) ==========

        /// <summary>
        /// Extract all real download links from a game page.
        /// Handles hosters/drive/uploads/torrent/magnet/1fichier/gofile/mega.
        /// </summary>
        public static List<DownloadLink> ParseDownloadLinks(string html)
        {
            var links = new List<DownloadLink>();
            var seen = new HashSet<string>();

            void Add(string url, string label)
            {
                var normalized = url.StartsWith("//") ? "https:" + url : url;
                if (!seen.Add(normalized)) return;
                links.Add(new DownloadLink { Url = normalized, Label = label, Name = label, Type = label });
            }

            // Button-style links
            var btnRe = new Regex(
                @"<a[^>]*href=""([^""]+)""[^>]*class=""[^""]*(?:btn|button|download)[^""]*""[^>]*>([^<]*)</a>",
                RegexOptions.IgnoreCase);
            foreach (Match m in btnRe.Matches(html))
            {
                var href = m.Groups[1].Value;
                if (href.Contains("online-fix.me") || href.StartsWith("magnet:") ||
                    href.Contains("1fichier.com") || href.Contains("mediafire.com") ||
                    href.Contains("gofile.io") || href.Contains("mega.nz") || href.Contains("pixeldrain.com"))
                {
                    Add(href, LabelFor(href));
                }
            }

            // Direct patterns (mirrors onlinefix.ts)
            var directPatterns = new (Regex re, string label)[]
            {
                (new Regex(@"href=""(https://hosters\.online-fix\.me[^""]+)""", RegexOptions.IgnoreCase), "Online-Fix Hosters"),
                (new Regex(@"href=""(https://drive\.online-fix\.me[^""]+)""", RegexOptions.IgnoreCase), "Online-Fix Drive"),
                (new Regex(@"href=""(https://uploads\.online-fix\.me[^""]*/uploads/[^""]+)""", RegexOptions.IgnoreCase), "Online-Fix Uploads"),
                (new Regex(@"href=""(https://uploads\.online-fix\.me[^""]*/torrents/[^""]+)""", RegexOptions.IgnoreCase), "Torrent"),
                (new Regex(@"href=""(magnet:[^""]+)""", RegexOptions.IgnoreCase), "Magnet Link"),
                (new Regex(@"href=""(https://1fichier\.com[^""]+)""", RegexOptions.IgnoreCase), "1Fichier"),
                (new Regex(@"href=""(https://gofile\.io[^""]+)""", RegexOptions.IgnoreCase), "GoFile"),
                (new Regex(@"href=""(https://mega\.nz[^""]+)""", RegexOptions.IgnoreCase), "Mega")
            };
            foreach (var (re, label) in directPatterns)
                foreach (Match m in re.Matches(html)) Add(m.Groups[1].Value, label);

            return links;
        }

        private static string LabelFor(string href)
        {
            if (href.Contains("hosters")) return "Online-Fix Hosters";
            if (href.Contains("drive")) return "Online-Fix Drive";
            if (href.Contains("uploads")) return "Online-Fix Uploads";
            if (href.StartsWith("magnet:")) return "Magnet Link";
            if (href.Contains("torrent")) return "Torrent";
            return "Download Link";
        }

        // ========== GAME DETAILS (ported from getDownloadInfo) ==========

        /// <summary>
        /// Fetch a game page and extract: title, image, description, all 4 download
        /// endpoints, password, version, and file size.
        /// </summary>
        public async Task<OnlineFixGame?> GetGameDetailsAsync(string pageUrl)
        {
            try
            {
                await WarmCookiesAsync(); // critical: avoids 401 on download links
                var html = await RequestAsync(pageUrl);
                var game = new OnlineFixGame { Url = pageUrl, Password = "online-fix.me" };

                // Title from <h1> or og:title
                var h1 = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (h1.Success)
                    game.Title = WebUtility.HtmlDecode(Regex.Replace(h1.Groups[1].Value, "<[^>]+>", "").Trim());
                if (string.IsNullOrEmpty(game.Title))
                {
                    var og = Regex.Match(html, @"<meta[^>]*property=""og:title""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (og.Success) game.Title = WebUtility.HtmlDecode(og.Groups[1].Value);
                }
                if (string.IsNullOrEmpty(game.Title)) game.Title = ExtractTitleFromUrl(pageUrl);

                // Image from og:image or data-src poster
                var ogImg = Regex.Match(html, @"<meta[^>]*property=""og:image""[^>]*content=""([^""]+)""", RegexOptions.IgnoreCase);
                if (ogImg.Success) game.ImageUrl = ogImg.Groups[1].Value;
                if (string.IsNullOrEmpty(game.ImageUrl))
                {
                    var poster = Regex.Match(html, @"data-src=""(https://online-fix\.me/uploads/posts/[^""]+\.jpg)""", RegexOptions.IgnoreCase);
                    if (poster.Success) game.ImageUrl = poster.Groups[1].Value;
                }

                // Description
                var desc = Regex.Match(html, @"<div[^>]*class=""[^""]*(?:full-text|full-story|description)[^""]*""[^>]*>(.*?)</div>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (desc.Success)
                {
                    var text = WebUtility.HtmlDecode(Regex.Replace(desc.Groups[1].Value, "<[^>]+>", " ").Trim());
                    game.Description = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
                }

                // Download links
                game.DownloadLinks = ParseDownloadLinks(html);

                // Game version
                var ver = Regex.Match(html, @"Game version:\s*([^<]+)", RegexOptions.IgnoreCase);
                if (ver.Success) game.GameVersion = ver.Groups[1].Value.Trim();

                // Password (English or Russian)
                foreach (var pRe in new[] { @"password[:\s]+([^<\s]+)", @"пароль[:\s]+([^<\s]+)" })
                {
                    var pm = Regex.Match(html, pRe, RegexOptions.IgnoreCase);
                    if (pm.Success) { game.Password = pm.Groups[1].Value; break; }
                }

                // File size
                var sm = Regex.Match(html, @"(\d+[.,]?\d*\s*(?:MB|GB))", RegexOptions.IgnoreCase);
                if (sm.Success) game.FileSize = sm.Groups[1].Value;

                game.Id = ExtractIdFromUrl(pageUrl);
                game.Slug = ExtractSlugFromUrl(pageUrl);
                return game;
            }
            catch (Exception ex)
            {
                LogMsg($"[GET] error {pageUrl}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Scrape a category listing for browsing (with posters).</summary>
        public async Task<List<OnlineFixGame>> ScrapeCategoryAsync(string category)
        {
            var games = new List<OnlineFixGame>();
            try
            {
                await WarmCookiesAsync();
                var url = $"{Base}/games/{category}/";
                var html = await RequestAsync(url);
                var found = AttachImages(html, ParseGames(html)).DistinctBy(g => g.Path).Take(60);

                // Also crawl a few extra pages for variety
                var postHeaders = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-www-form-urlencoded",
                    ["X-Requested-With"] = "XMLHttpRequest",
                    ["Referer"] = url
                };
                var extra = new List<IndexGame>();
                for (int page = 1; page <= 3; page++)
                {
                    try
                    {
                        var json = await RequestAsync(url, HttpMethod.Post, postHeaders, $"show_more={page}");
                        var content = JObject.Parse(json)?["content"]?.Value<string>();
                        if (string.IsNullOrEmpty(content)) break;
                        extra.AddRange(AttachImages(content, ParseGames(content)));
                    }
                    catch { break; }
                }

                foreach (var g in found.Concat(extra).DistinctBy(g => g.Path))
                {
                    games.Add(new OnlineFixGame
                    {
                        Id = ExtractIdFromUrl(g.Url),
                        Title = !string.IsNullOrEmpty(g.Title) ? g.Title : ExtractTitleFromUrl(g.Url),
                        Url = g.Url,
                        Category = category,
                        ImageUrl = g.Image,
                        Slug = g.Slug
                    });
                }
            }
            catch (Exception ex) { LogMsg($"[CAT] {category}: {ex.Message}"); }
            return games;
        }

        /// <summary>Scrape multiple categories concurrently (for the browse view).</summary>
        public async Task<List<OnlineFixGame>> ScrapeAllAsync()
        {
            await WarmCookiesAsync();
            var tasks = Categories.Select(c => ScrapeCategoryAsync(c));
            var results = await Task.WhenAll(tasks);
            return results.SelectMany(r => r).DistinctBy(g => g.Url).ToList();
        }

        // ========== URL HELPERS ==========

        private static string ExtractIdFromUrl(string url)
        {
            var m = Regex.Match(url, @"/(\d+)-[^/]+\.html$");
            return m.Success ? m.Groups[1].Value : Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string ExtractSlugFromUrl(string url)
        {
            var m = Regex.Match(url, @"/\d+-([^/]+)\.html$");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string ExtractTitleFromUrl(string url)
        {
            var slug = ExtractSlugFromUrl(url);
            if (string.IsNullOrEmpty(slug)) return "Unknown Game";
            // strip trailing language markers
            slug = Regex.Replace(slug, @"-po-seti.*$", "", RegexOptions.IgnoreCase);
            slug = Regex.Replace(slug, @"-online.*$", "", RegexOptions.IgnoreCase);
            return string.Join(" ", slug.Split('-')
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
        }

        public OnlineFixIndexFile? GetIndex() => _index;

        // ========== SECOND-LEVEL SCRAPER (follow hosters/drive pages) ==========

        /// <summary>
        /// Follow an online-fix hosters/drive/uploads endpoint page and extract
        /// the ACTUAL file-hosting links (rootz.so, mega, gofile, etc.).
        /// hosters.online-fix.me:2053/{Game} returns an HTML listing page.
        /// </summary>
        public async Task<List<DownloadLink>> ResolveHosterPageAsync(string hosterUrl, string? referer = null)
        {
            var links = new List<DownloadLink>();
            var seen = new HashSet<string>();
            try
            {
                await WarmCookiesAsync();
                // Use the originating game page as referer — hosters.online-fix.me:2053 returns
                // 401 "User not recognized" without it. Fall back to the site root.
                var extra = new Dictionary<string, string> { ["Referer"] = referer ?? Base + "/" };
                var html = await RequestAsync(hosterUrl, null, extra);

                // Extract all external hrefs that look like file hosters
                var hrefRe = new Regex(@"href=""(https?://[^""]+)""", RegexOptions.IgnoreCase);
                foreach (Match m in hrefRe.Matches(html))
                {
                    var href = m.Groups[1].Value;
                    // Skip internal/static/navigation links
                    if (href.Contains("online-fix.me") || href.Contains("/static/") ||
                        href.Contains("favicon") || href.Contains(".css") || href.Contains(".ico"))
                        continue;

                    var host = new Uri(href).Host.Replace("www.", "");
                    if (IsFileHoster(host) && seen.Add(href))
                    {
                        links.Add(new DownloadLink
                        {
                            Url = href,
                            Label = host,
                            Name = host,
                            Type = host
                        });
                    }
                }

                // For uploads.online-fix.me pages, look for direct file links (.rar/.zip/.7z)
                var fileRe = new Regex(@"href=""([^""]+\.(?:rar|zip|7z|001))""", RegexOptions.IgnoreCase);
                foreach (Match m in fileRe.Matches(html))
                {
                    var href = m.Groups[1].Value;
                    if (href.StartsWith("/")) href = "https://uploads.online-fix.me:2053" + href;
                    if (seen.Add(href))
                        links.Add(new DownloadLink { Url = href, Label = "Direct File", Name = "Direct File", Type = "Direct" });
                }
            }
            catch (Exception ex) { LogMsg($"[HOSTER] {hosterUrl}: {ex.Message}"); }
            return links;
        }

        private static bool IsFileHoster(string host) =>
            host.Contains("rootz.so") || host.Contains("mega.nz") || host.Contains("mega.co.nz") ||
            host.Contains("gofile.io") || host.Contains("mediafire.com") || host.Contains("1fichier.com") ||
            host.Contains("pixeldrain.com") || host.Contains("drive.google.com") || host.Contains("buzzheavier") ||
            host.Contains("katfile") || host.Contains("rapidgator") || host.Contains("nitroflare") ||
            host.Contains("turbobit") || host.Contains("dropapk") || host.Contains("mixdrop") ||
            host.Contains("uploadhaven") || host.Contains("workupload") || host.Contains("we.tl") ||
            host.Contains("send.cm") || host.Contains("usersdrive") || host.Contains("megaup");

        /// <summary>
        /// Get ALL resolved download links for a game: first-level (hosters/drive/uploads/torrent
        /// endpoints) AND second-level (the actual file-hosting links from each endpoint).
        /// </summary>
        public async Task<List<DownloadLink>> ResolveAllDownloadsAsync(OnlineFixGame game)
        {
            var all = new List<DownloadLink>(game.DownloadLinks);

            // For each online-fix endpoint, follow it to get real file links
            foreach (var link in game.DownloadLinks.ToList())
            {
                if (link.Url.Contains("hosters.online-fix.me") ||
                    link.Url.Contains("drive.online-fix.me") ||
                    link.Url.Contains("uploads.online-fix.me"))
                {
                    var resolved = await ResolveHosterPageAsync(link.Url, game.Url);
                    foreach (var r in resolved)
                    {
                        r.Label = $"{link.Label} → {r.Label}";
                        all.Add(r);
                    }
                }
            }

            return all.DistinctBy(l => l.Url).ToList();
        }
    }
}

