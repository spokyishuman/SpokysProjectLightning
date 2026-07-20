using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpokysProjectLightning.Services
{
    public class SteamDaddyService
    {
        private const string RepoOwner = "Contrary7nit";
        private const string RepoName = "SteamDaddy";
        private const string ApiBase = "https://steamdaddy.duckdns.org/api";
        private const string LatestReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        private readonly HttpClient _http;
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "SteamDaddy");

        public string? ApiKey { get; set; }
        public string? CurrentVersion { get; private set; }
        public string? LatestVersion { get; private set; }

        public SteamDaddyService(string? apiKey = null)
        {
            ApiKey = apiKey;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL/1.0");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<(string version, string downloadUrl)> CheckLatestReleaseAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(LatestReleaseUrl);
                var data = JObject.Parse(json);
                var tag = data["tag_name"]?.Value<string>() ?? "0.0.0";
                var assets = data["assets"] as JArray;
                var exeAsset = assets?.FirstOrDefault(a =>
                    a["name"]?.Value<string>()?.Equals("SteamDaddy.exe", StringComparison.OrdinalIgnoreCase) == true);
                var url = exeAsset?["browser_download_url"]?.Value<string>() ?? "";
                LatestVersion = tag;
                return (tag, url);
            }
            catch
            {
                return ("0.0.0", "");
            }
        }

        public async Task<bool> DownloadLatestAsync()
        {
            try
            {
                var (version, url) = await CheckLatestReleaseAsync();
                if (string.IsNullOrEmpty(url)) return false;

                Directory.CreateDirectory(AppDataDir);
                var exeBytes = await _http.GetByteArrayAsync(url);
                var destPath = Path.Combine(AppDataDir, "SteamDaddy.exe");
                await File.WriteAllBytesAsync(destPath, exeBytes);

                var verPath = Path.Combine(AppDataDir, "version.txt");
                await File.WriteAllTextAsync(verPath, version);
                CurrentVersion = version;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string? GetExePath()
        {
            var path = Path.Combine(AppDataDir, "SteamDaddy.exe");
            return File.Exists(path) ? path : null;
        }

        public string? GetInstalledVersion()
        {
            var verPath = Path.Combine(AppDataDir, "version.txt");
            if (File.Exists(verPath))
            {
                CurrentVersion = File.ReadAllText(verPath).Trim();
                return CurrentVersion;
            }
            return null;
        }

        public bool HasUpdateAvailable()
        {
            if (LatestVersion == null || CurrentVersion == null) return false;
            var latest = ParseVersion(LatestVersion);
            var current = ParseVersion(CurrentVersion);
            if (latest == null || current == null) return false;
            return latest > current;
        }

        public void Launch()
        {
            var exe = GetExePath();
            if (exe != null)
            {
                try { Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true }); }
                catch { }
            }
        }

        public async Task<SteamDaddyManifestResult?> FetchManifestAsync(string appId)
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;

            try
            {
                var url = $"{ApiBase}/download/{appId}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                request.Headers.Add("User-Agent", "SpokysPL/1.0");

                var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode) return null;

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                if (bytes.Length == 0) return null;

                var result = new SteamDaddyManifestResult
                {
                    AppId = appId,
                    RawData = bytes,
                    ContentType = resp.Content.Headers.ContentType?.MediaType ?? ""
                };

                // If response is a zip, extract manifest/lua files
                if (result.ContentType.Contains("zip") || HasZipHeader(bytes))
                {
                    result.IsZip = true;
                    result.ExtractedFiles = ExtractManifestZip(bytes);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public async Task<SteamDaddyUsage?> CheckUsageAsync()
        {
            if (string.IsNullOrEmpty(ApiKey)) return null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/usage");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode) return null;

                var body = await resp.Content.ReadAsStringAsync();
                var json = JObject.Parse(body);
                return new SteamDaddyUsage
                {
                    Total = json["total"]?.Value<int>() ?? 0,
                    Used = json["used"]?.Value<int>() ?? 0,
                    Remaining = json["remaining"]?.Value<int>() ?? 0,
                    Limit = json["limit"]?.Value<int>() ?? 20,
                    ResetDate = json["reset_date"]?.Value<string>() ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        private List<ExtractedManifestFile> ExtractManifestZip(byte[] zipBytes)
        {
            var files = new List<ExtractedManifestFile>();
            try
            {
                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    using var entryStream = entry.Open();
                    using var memStream = new MemoryStream();
                    entryStream.CopyTo(memStream);
                    files.Add(new ExtractedManifestFile
                    {
                        FileName = entry.Name,
                        Data = memStream.ToArray(),
                        IsLua = entry.Name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase),
                        IsManifest = entry.Name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase),
                        IsVdf = entry.Name.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch { }
            return files;
        }

        private static bool HasZipHeader(byte[] data)
        {
            return data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B;
        }

        private static Version? ParseVersion(string v)
        {
            var clean = v.TrimStart('v', 'V');
            if (Version.TryParse(clean, out var ver))
            {
                var maj = ver.Major;
                var min = ver.Minor < 0 ? 0 : ver.Minor;
                var build = ver.Build < 0 ? 0 : ver.Build;
                var rev = ver.Revision < 0 ? 0 : ver.Revision;
                return new Version(maj, min, build, rev);
            }
            return null;
        }
    }

    public class SteamDaddyManifestResult
    {
        public string AppId { get; set; } = "";
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "";
        public bool IsZip { get; set; }
        public List<ExtractedManifestFile> ExtractedFiles { get; set; } = new();
    }

    public class ExtractedManifestFile
    {
        public string FileName { get; set; } = "";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public bool IsLua { get; set; }
        public bool IsManifest { get; set; }
        public bool IsVdf { get; set; }
    }

    public class SteamDaddyUsage
    {
        public int Total { get; set; }
        public int Used { get; set; }
        public int Remaining { get; set; }
        public int Limit { get; set; } = 20;
        public string ResetDate { get; set; } = "";
        public string Display => $"{Used}/{Limit} used — {Remaining} remaining";
    }
}
