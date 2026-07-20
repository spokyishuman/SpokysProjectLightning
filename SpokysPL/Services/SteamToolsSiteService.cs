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

                return await _http.GetByteArrayAsync(downloadUrl);
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
    }
}
