using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    public class SteamService
    {
        private readonly HttpClient _httpClient;
        private static readonly string SteamApiBase = "https://store.steampowered.com/api";

        public SteamService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }

        public async Task<List<ManifestInfo>> GetRecommendedPaidGamesAsync()
        {
            var recommended = new List<ManifestInfo>();
            try
            {
                var response = await _httpClient.GetStringAsync($"{SteamApiBase}/featuredcategories?cc=us&l=en");
                var json = JObject.Parse(response);
                var categories = new[] { "specials", "coming_soon", "top_sellers", "new_releases" };

                foreach (var category in categories)
                {
                    if (json[category] != null)
                    {
                        var items = json[category]?["items"] as JArray;
                        if (items != null)
                        {
                            foreach (var item in items.Take(10))
                            {
                                int appId = item["id"]?.Value<int>() ?? 0;
                                if (appId <= 0) continue;
                                var finalPrice = item["final_price"]?.Value<int>() ?? -1;
                                if (finalPrice <= 0) continue;
                                recommended.Add(new ManifestInfo
                                {
                                    AppId = appId.ToString(),
                                    Name = item["name"]?.Value<string>() ?? $"App {appId}",
                                    ImageUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
                                    IsRecommended = true,
                                    ManifestUrl = $"https://fares.top/app/{appId}"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Steam recommendations: {ex.Message}");
                recommended.AddRange(GetFallbackGames());
            }

            return recommended.DistinctBy(m => m.AppId).ToList();
        }

        public async Task<List<ManifestInfo>> SearchGameAsync(string query)
        {
            var results = new List<ManifestInfo>();
            try
            {
                var response = await _httpClient.GetStringAsync(
                    $"{SteamApiBase}/storesearch?term={Uri.EscapeDataString(query)}&cc=us&l=en");
                var json = JObject.Parse(response);

                if (json["items"] is JArray items)
                {
                    foreach (var item in items.Take(20))
                    {
                        int appId = item["id"]?.Value<int>() ?? 0;
                        if (appId > 0)
                        {
                            results.Add(new ManifestInfo
                            {
                                AppId = appId.ToString(),
                                Name = item["name"]?.Value<string>() ?? "Unknown",
                                ImageUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
                                ManifestUrl = $"https://fares.top/app/{appId}"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching Steam: {ex.Message}");
            }
            return results;
        }

        /// <summary>
        /// Returns the fares.top app page for the given App ID. fares.top serves manifests
        /// through its JSON API (see <see cref="FaresService"/>), not via guessable file URLs,
        /// so we no longer probe for manifest/<id>.txt style paths (those returned 404). The
        /// reliable, always-valid URL is the app page, which the UI opens in the browser.
        /// </summary>
        public Task<string?> GetManifestFromFaresTopAsync(string appId)
        {
            return Task.FromResult<string?>($"https://fares.top/app/{appId}");
        }

        private const string CdnBase = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps";

        public static string GameImageUrl(string appId) => $"{CdnBase}/{appId}/library_600x900.jpg";
        public static string GameHeaderUrl(string appId) => $"{CdnBase}/{appId}/header.jpg";

        /// <summary>
        /// Locate the Steam install folder. Tries, in order:
        /// 1. Registry (HKCU/SOFTWARE/Valve/Steam/SteamPath) — the canonical location.
        /// 2. Well-known Program Files paths (incl. the localized x86 folder).
        /// 3. A scan of every fixed/removable drive for a steamapps folder.
        /// </summary>
        public static string? FindSteamPath()
        {
            // 1. Registry (most reliable, covers custom installs on any drive)
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var regPath = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(regPath) &&
                    Directory.Exists(regPath) &&
                    File.Exists(Path.Combine(regPath, "steam.exe")))
                {
                    return NormalizeSeparators(regPath);
                }
            }
            catch { }

            // 2. Standard Program Files locations
            var candidates = new List<string>
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            };
            foreach (var path in candidates.Where(Directory.Exists))
            {
                if (File.Exists(Path.Combine(path, "steam.exe")))
                    return path;
            }

            // 3. Scan every drive for the Steam install
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var roots = new[]
                    {
                        Path.Combine(drive.RootDirectory.FullName, "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Program Files", "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Games", "Steam")
                    };
                    foreach (var path in roots)
                    {
                        if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                            return path;
                    }
                }
            }
            catch { }

            return null;
        }

        private static string NormalizeSeparators(string path)
            => path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

        public static List<string> GetSteamLibraryFolders()
        {
            var folders = new List<string>();
            var steamPath = FindSteamPath();
            if (steamPath == null) return folders;

            // Add the primary library's steamapps folder (NOT .../steamapps/common)
            folders.Add(Path.Combine(steamPath, "steamapps"));

            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    var content = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        var libPath = NormalizeSeparators(match.Groups[1].Value);
                        var steamapps = Path.Combine(libPath, "steamapps");
                        if (Directory.Exists(steamapps) && !folders.Contains(steamapps))
                            folders.Add(steamapps);
                    }
                }
                catch { }
            }

            return folders.Distinct().ToList();
        }

        public static List<ManifestInfo> GetFallbackGames()
        {
            var fallbackIds = new (string id, string name)[]
            {
                ("1245620", "ELDEN RING"),
                ("2358720", "Black Myth: Wukong"),
                ("990080", "Hogwarts Legacy"),
                ("1174180", "Red Dead Redemption 2"),
                ("271590", "Grand Theft Auto V"),
                ("1086940", "Baldur's Gate 3"),
                ("1551360", "Forza Horizon 5"),
                ("1091500", "Cyberpunk 2077"),
                ("1716740", "Starfield"),
                ("1938090", "Call of Duty\u00ae")
            };

            return fallbackIds.Select(f => new ManifestInfo
            {
                AppId = f.id,
                Name = f.name,
                ImageUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{f.id}/library_600x900.jpg",
                IsRecommended = true,
                ManifestUrl = $"https://fares.top/app/{f.id}"
            }).ToList();
        }
    }
}

