using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpokysProjectLightning.Services
{
    public class RyuuFixEntry
    {
        public string AppId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Href { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Size { get; set; } = "";
        public List<string> Badges { get; set; } = new();
    }

    public class RyuuFixesService
    {
        private const string FixesJsonUrl = "https://generator.ryuu.lol/files/fixes.json";
        private const string BaseUrl = "https://generator.ryuu.lol";

        private readonly HttpClient _http;
        private List<RyuuFixEntry>? _cache;
        private DateTime _lastFetch = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public RyuuFixesService()
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
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _http.DefaultRequestHeaders.Add("Accept", "*/*");
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<List<RyuuFixEntry>> GetAllFixesAsync()
        {
            if (_cache != null && DateTime.UtcNow - _lastFetch < CacheDuration)
                return _cache;

            try
            {
                var json = await _http.GetStringAsync(FixesJsonUrl);
                var arr = JArray.Parse(json);
                var entries = new List<RyuuFixEntry>();

                foreach (var item in arr)
                {
                    var appId = (string?)item["appid"] ?? "";
                    var name = (string?)item["name"] ?? "";
                    var fixes = item["fixes"] as JArray;
                    if (fixes == null || fixes.Count == 0) continue;

                    foreach (var fix in fixes)
                    {
                        var href = (string?)fix["href"] ?? "";
                        entries.Add(new RyuuFixEntry
                        {
                            AppId = appId,
                            Name = name,
                            Href = href,
                            FileName = (string?)fix["filename"] ?? "",
                            Size = (string?)fix["size"] ?? "",
                            Badges = fix["badges"]?.Select(b => (string?)b?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new()
                        });
                    }
                }

                _cache = entries;
                _lastFetch = DateTime.UtcNow;
                return entries;
            }
            catch
            {
                return _cache ?? new();
            }
        }

        public async Task<List<RyuuFixEntry>> SearchFixesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return await GetAllFixesAsync();

            var all = await GetAllFixesAsync();
            var q = query.Trim().ToLowerInvariant();
            return all.Where(f =>
                f.Name.ToLowerInvariant().Contains(q) ||
                f.AppId.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                f.AppId.Contains(q)
            ).ToList();
        }

        public async Task<RyuuFixEntry?> GetFixForAppAsync(string appId)
        {
            var all = await GetAllFixesAsync();
            return all.FirstOrDefault(f =>
                f.AppId.Equals(appId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public async Task<byte[]?> DownloadFixAsync(string href)
        {
            var fileName = Path.GetFileName(href.TrimEnd('/').Replace("%20", " "));
            var urls = new List<string>();

            // Collect all possible URL shapes
            if (href.StartsWith("http"))
            {
                urls.Add(href);
                // Also try with /files/ prefix in case href is a filename
                if (!string.IsNullOrEmpty(fileName))
                    urls.Add($"{BaseUrl}/files/{fileName}");
            }
            else
            {
                var clean = href.TrimStart('/');
                // e.g. "33 Immortals.zip" → BaseUrl/33 Immortals.zip
                urls.Add($"{BaseUrl}/{clean}");
                // e.g. "33 Immortals.zip" → BaseUrl/files/33 Immortals.zip
                urls.Add($"{BaseUrl}/files/{clean}");
                // If href looks like a path "some/path/file.zip", also try just the filename
                if (clean.Contains('/') && !string.IsNullOrEmpty(fileName))
                {
                    urls.Add($"{BaseUrl}/{fileName}");
                    urls.Add($"{BaseUrl}/files/{fileName}");
                }
            }

            // Remove duplicates
            urls = urls.Distinct().ToList();

            foreach (var url in urls)
            {
                try
                {
                    var escaped = url.Replace(" ", "%20");
                    using var req = new HttpRequestMessage(HttpMethod.Get, escaped);
                    req.Headers.Referrer = new Uri(BaseUrl);
                    // Full browser-like headers
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
                    req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                    req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                    req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                    req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    req.Headers.TryAddWithoutValidation("DNT", "1");

                    var resp = await _http.SendAsync(req);
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";

                    // If server returned HTML (Discord login page), skip this URL
                    if (ct.Contains("text/html") || ct.Contains("text/plain"))
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        if (body.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("login", StringComparison.OrdinalIgnoreCase))
                            continue; // not the real file, try next URL
                    }

                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsByteArrayAsync();
                }
                catch
                {
                    continue;
                }
            }
            return null;
        }
    }
}

