using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpokysProjectVercel.Services
{
    public class SteamManifestResult
    {
        public string AppId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string LuaUrl { get; set; } = "";
        public string KeyVdfUrl { get; set; } = "";
    }

    public class SteamToolsGamesService
    {
        private const string BaseUrl = "https://steamtools.games";
        private readonly HttpClient _http;

        public SteamToolsGamesService()
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

        public async Task<List<(string id, string name, string image)>> SearchGamesAsync(string query)
        {
            try
            {
                var url = $"{BaseUrl}/api/search?query={Uri.EscapeDataString(query)}";
                var json = await _http.GetStringAsync(url);
                var data = JObject.Parse(json);
                var results = data["data"]?["results"] as JArray;
                if (results == null) return new();

                return results.Select(r => (
                    id: (string?)r["id"] ?? "",
                    name: (string?)r["name"] ?? "",
                    image: (string?)r["image"] ?? ""
                )).Where(r => !string.IsNullOrEmpty(r.id)).ToList();
            }
            catch
            {
                return new();
            }
        }

        public async Task<SteamManifestResult?> GenerateManifestAsync(string appId, string branch = "public")
        {
            try
            {
                var payload = new JObject { ["appId"] = appId, ["branch"] = branch };
                var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{BaseUrl}/api/generate", content);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                if ((int?)data["code"] != 0) return null;

                var d = data["data"];
                if (d == null) return null;

                return new SteamManifestResult
                {
                    AppId = (string?)d["appId"] ?? appId,
                    GameName = (string?)d["gameName"] ?? appId,
                    DownloadUrl = (string?)d["downloadUrl"] ?? "",
                    LuaUrl = (string?)d["luaUrl"] ?? "",
                    KeyVdfUrl = (string?)d["keyVdfUrl"] ?? "",
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<byte[]?> DownloadFileAsync(string url)
        {
            try { return await _http.GetByteArrayAsync(url); }
            catch { return null; }
        }
    }
}

