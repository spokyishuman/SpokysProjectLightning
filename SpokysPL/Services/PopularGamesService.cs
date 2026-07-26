using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    public static class PopularGamesService
    {
        private static readonly HttpClient Client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static PopularGamesService()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        }

        public static async Task<List<PopularGame>> GetPopularGamesAsync(string source = "all", int limit = 20)
        {
            var games = new List<PopularGame>();

            if (source == "all" || source == "steamrip")
            {
                games.AddRange(await GetPopularSteamRipGamesAsync(limit));
            }

            if (source == "all" || source == "dodi")
            {
                games.AddRange(await GetPopularDodiGamesAsync(limit));
            }

            return games.Take(limit).ToList();
        }

        private static async Task<List<PopularGame>> GetPopularSteamRipGamesAsync(int limit)
        {
            var games = new List<PopularGame>();

            try
            {
                // Scrape the main page for recent/popular games
                var urls = new[]
                {
                    "https://steamrip.com/",
                    "https://steamrip.com/category/games/",
                    "https://steamrip.com/category/repacks/"
                };

                foreach (var url in urls)
                {
                    try
                    {
                        var html = await Client.GetStringAsync(url);
                        var pageGames = ParseSteamRipGames(html, url);
                        games.AddRange(pageGames);
                    }
                    catch { }
                }

                // Also try to get games from a popular posts page if available
                try
                {
                    var popularHtml = await Client.GetStringAsync("https://steamrip.com/popular/");
                    games.AddRange(ParseSteamRipGames(popularHtml, "https://steamrip.com/popular/"));
                }
                catch { }
            }
            catch { }

            return games.DistinctBy(g => g.Url).Take(limit).ToList();
        }

        private static List<PopularGame> ParseSteamRipGames(string html, string baseUrl)
        {
            var games = new List<PopularGame>();
            var seenUrls = new HashSet<string>();

            // Multiple patterns to catch different SteamRIP layouts
            var patterns = new[]
            {
                // Standard post links
                @"<a[^>]*href=""(https?://steamrip\.com/[^""]*)""[^>]*>(?:<h[^>]*>)?([^<]*)(?:</h[^>]*>)?</a>",
                // Article links
                @"<article[^>]*>.*?<a[^>]*href=""([^""]*)""[^>]*>.*?<h[^>]*>([^<]*)</h[^>]*>.*?</article>",
                // Grid items
                @"<div[^>]*class=""[^""]*(?:post|grid|item)[^""]*""[^>]*>.*?<a[^>]*href=""([^""]*)""[^>]*>.*?<h[^>]*>([^<]*)</h[^>]*>",
                // Recent posts widget
                @"<li[^>]*>.*?<a[^>]*href=""(https?://steamrip\.com/[^""]*)""[^>]*>([^<]*)</a>"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count < 3) continue;
                    
                    var url = m.Groups[1].Value.Trim();
                    var name = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
                    
                    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name)) continue;
                    if (url.Contains("#") || url.Contains("?replytocom=") || url.Contains("/wp-admin") || url.Contains("/tag/") || url.Contains("/category/") || url.Contains("/author/")) continue;
                    if (seenUrls.Contains(url)) continue;
                    if (name.Length < 3 || name.Length > 100) continue;

                    // Filter out non-game links
                    var skipKeywords = new[] { "home", "about", "contact", "privacy", "dmca", "request", "search", "login", "register", "category", "tag", "page", "feed", "rss" };
                    if (skipKeywords.Any(k => url.Contains($"/{k}/") || url.EndsWith($"/{k}"))) continue;

                    seenUrls.Add(url);
                    games.Add(new PopularGame
                    {
                        Name = CleanGameName(name),
                        Url = url,
                        Source = "steamrip",
                        Icon = "🎮"
                    });
                }
            }

            return games;
        }

        private static async Task<List<PopularGame>> GetPopularDodiGamesAsync(int limit)
        {
            var games = new List<PopularGame>();

            try
            {
                // DODI repacks main page
                var urls = new[]
                {
                    "https://dodi-repacks.site/",
                    "https://dodi-repacks.site/category/games/",
                    "https://dodi-repacks.site/category/repacks/"
                };

                foreach (var url in urls)
                {
                    try
                    {
                        var html = await Client.GetStringAsync(url);
                        var pageGames = ParseDodiGames(html);
                        games.AddRange(pageGames);
                    }
                    catch { }
                }
            }
            catch { }

            return games.DistinctBy(g => g.Url).Take(limit).ToList();
        }

        private static List<PopularGame> ParseDodiGames(string html)
        {
            var games = new List<PopularGame>();
            var seenUrls = new HashSet<string>();

            var patterns = new[]
            {
                @"<a[^>]*href=""(https?://dodi-repacks\.site/[^""]*)""[^>]*>.*?<h[^>]*>([^<]*)</h[^>]*>",
                @"<article[^>]*>.*?<a[^>]*href=""([^""]*)""[^>]*>.*?<h[^>]*>([^<]*)</h[^>]*>.*?</article>",
                @"<div[^>]*class=""[^""]*(?:post|grid|item)[^""]*""[^>]*>.*?<a[^>]*href=""([^""]*)""[^>]*>.*?<h[^>]*>([^<]*)</h[^>]*>",
                @"<li[^>]*>.*?<a[^>]*href=""(https?://dodi-repacks\.site/[^""]*)""[^>]*>([^<]*)</a>"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    if (m.Groups.Count < 3) continue;
                    
                    var url = m.Groups[1].Value.Trim();
                    var name = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
                    
                    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name)) continue;
                    if (url.Contains("#") || url.Contains("/category/") || url.Contains("/tag/") || url.Contains("/author/") || url.Contains("/page/")) continue;
                    if (seenUrls.Contains(url)) continue;
                    if (name.Length < 3 || name.Length > 100) continue;

                    var skipKeywords = new[] { "home", "about", "contact", "privacy", "dmca", "request", "search", "login", "register", "category", "tag", "page", "feed", "rss" };
                    if (skipKeywords.Any(k => url.Contains($"/{k}/") || url.EndsWith($"/{k}"))) continue;

                    seenUrls.Add(url);
                    games.Add(new PopularGame
                    {
                        Name = CleanGameName(name),
                        Url = url,
                        Source = "dodi",
                        Icon = "📦"
                    });
                }
            }

            return games;
        }

        private static string CleanGameName(string name)
        {
            name = System.Net.WebUtility.HtmlDecode(name);
            name = Regex.Replace(name, @"\s+", " ");
            name = name.Trim();
            
            // Remove common suffixes
            name = Regex.Replace(name, @"\s*[-|]\s*(?:Free Download|Download|PC|Repack|Crack|Full|Game|Latest|Update).*$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s*v?\d+\.\d+(\.\d+)?\s*$", "");
            name = Regex.Replace(name, @"\s*\([^)]*\)\s*$", "");
            
            return name.Trim();
        }

        // Fallback: Hardcoded popular games cache for when scraping fails
        public static List<PopularGame> GetFallbackPopularGames(int limit = 20)
        {
            var games = new List<PopularGame>
            {
                new() { Name = "Elden Ring", Url = "https://steamrip.com/elden-ring-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Cyberpunk 2077", Url = "https://steamrip.com/cyberpunk-2077-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Red Dead Redemption 2", Url = "https://steamrip.com/red-dead-redemption-2-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Baldur's Gate 3", Url = "https://steamrip.com/baldurs-gate-3-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Hogwarts Legacy", Url = "https://steamrip.com/hogwarts-legacy-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Resident Evil 4 Remake", Url = "https://steamrip.com/resident-evil-4-remake-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Alan Wake 2", Url = "https://steamrip.com/alan-wake-2-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "Starfield", Url = "https://steamrip.com/starfield-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "The Witcher 3 Next Gen", Url = "https://steamrip.com/the-witcher-3-wild-hunt-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "God of War Ragnarök", Url = "https://steamrip.com/god-of-war-ragnarok-free-download/", Source = "steamrip", Icon = "🎮" },
                new() { Name = "FIFA 24", Url = "https://dodi-repacks.site/fifa-24/", Source = "dodi", Icon = "📦" },
                new() { Name = "Call of Duty MW3 2023", Url = "https://dodi-repacks.site/call-of-duty-modern-warfare-iii-2023/", Source = "dodi", Icon = "📦" },
                new() { Name = "Assassin's Creed Mirage", Url = "https://dodi-repacks.site/assassins-creed-mirage/", Source = "dodi", Icon = "📦" },
                new() { Name = "Avatar Frontiers of Pandora", Url = "https://dodi-repacks.site/avatar-frontiers-of-pandora/", Source = "dodi", Icon = "📦" },
                new() { Name = "Mortal Kombat 1", Url = "https://dodi-repacks.site/mortal-kombat-1/", Source = "dodi", Icon = "📦" },
                new() { Name = "Lies of P", Url = "https://dodi-repacks.site/lies-of-p/", Source = "dodi", Icon = "📦" },
                new() { Name = "Armored Core VI", Url = "https://dodi-repacks.site/armored-core-vi-fires-of-rubicon/", Source = "dodi", Icon = "📦" },
                new() { Name = "Remnant 2", Url = "https://dodi-repacks.site/remnant-ii/", Source = "dodi", Icon = "📦" },
                new() { Name = "Payday 3", Url = "https://dodi-repacks.site/payday-3/", Source = "dodi", Icon = "📦" },
                new() { Name = "Atomic Heart", Url = "https://dodi-repacks.site/atomic-heart/", Source = "dodi", Icon = "📦" },
            };

            return games.Take(limit).ToList();
        }
    }
}