using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpokysProjectLightning.Services
{
    public class SteamToolsSiteService
    {
        private const string BaseUrl = "https://steamtools.site";
        private readonly HttpClient _http;

        public SteamToolsSiteService()
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
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<List<(string id, string name, string image)>> SearchGamesAsync(string query)
        {
            try
            {
                var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}";
                var json = await _http.GetStringAsync(url);
                var data = JObject.Parse(json);
                var results = data["results"] as JArray;
                if (results == null) return new();

                return results.Select(r => (
                    id: r["id"]?.Value<string>() ?? "",
                    name: r["name"]?.Value<string>() ?? "",
                    image: r["image"]?.Value<string>() ?? ""
                )).Where(r => !string.IsNullOrEmpty(r.id)).ToList();
            }
            catch
            {
                return new();
            }
        }

        public async Task<byte[]?> DownloadManifestZipAsync(string appId)
        {
            try
            {
                var formContent = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("fileId", appId) });
                var resp = await _http.PostAsync($"{BaseUrl}/download", formContent);
                resp.EnsureSuccessStatusCode();
                var html = await resp.Content.ReadAsStringAsync();

                var downloadUrl = ExtractDownloadUrl(html);
                if (string.IsNullOrEmpty(downloadUrl)) return null;

                // tpi.li and similar shorteners return HTML with JS ads — HttpClient can't execute JS.
                // Check content type before returning bytes to skip ad pages gracefully.
                var content = await _http.GetByteArrayAsync(downloadUrl);

                // If response starts with "<html" or "<!DOCTYPE", it's an ad page, not a zip
                if (content.Length > 10)
                {
                    var header = System.Text.Encoding.UTF8.GetString(content, 0, Math.Min(20, content.Length));
                    if (header.StartsWith("<!", StringComparison.OrdinalIgnoreCase) ||
                        header.StartsWith("<ht", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try following a meta refresh or second href in the ad page
                        var metaUrl = ExtractMetaRefreshUrl(System.Text.Encoding.UTF8.GetString(content));
                        if (!string.IsNullOrEmpty(metaUrl))
                            return await _http.GetByteArrayAsync(metaUrl);
                        return null; // can't handle JS redirects
                    }
                }

                return content;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractDownloadUrl(string html)
        {
            var marker = "href=\"";
            var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            idx += marker.Length;
            var end = html.IndexOf("\"", idx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return "";
            return html.Substring(idx, end - idx);
        }

        private static string ExtractMetaRefreshUrl(string html)
        {
            var marker = "meta http-equiv=\"refresh\"";
            var idx = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var urlMarker = "url=";
            var urlIdx = html.IndexOf(urlMarker, idx + marker.Length, StringComparison.OrdinalIgnoreCase);
            if (urlIdx < 0) return "";
            urlIdx += urlMarker.Length;
            var end = html.IndexOf("\"", urlIdx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = html.IndexOf("'", urlIdx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = html.IndexOf(">", urlIdx, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return html.Substring(urlIdx).Trim();
            return html.Substring(urlIdx, end - urlIdx).Trim().Trim('\'', '"');
        }
    }
}
