using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpokysProjectLightning.Services
{
    public class OpenSteamToolService
    {
        private const string RepoOwner = "OpenSteam001";
        private const string RepoName = "OpenSteamTool";
        private const string ReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        private const string DefaultTomlPath = "opensteamtool.toml";

        private readonly HttpClient _http;
        private static readonly string DownloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysPL", "OpenSteamTool");

        public OpenSteamToolService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "OpenSteamTool-Manager/1.0");
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public string? CurrentVersion { get; private set; }
        public string? LatestVersion { get; private set; }

        public async Task<(string version, string releaseUrl, string zipUrl, long size)> CheckLatestReleaseAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(ReleaseUrl);
                var data = JObject.Parse(json);
                var tag = data["tag_name"]?.Value<string>() ?? "0.0.0";
                var htmlUrl = data["html_url"]?.Value<string>() ?? "";

                var assets = data["assets"] as JArray;
                var releaseAsset = assets?.FirstOrDefault(a =>
                    a["name"]?.Value<string>()?.Contains("-Release.zip") == true);
                var debugAsset = assets?.FirstOrDefault(a =>
                    a["name"]?.Value<string>()?.Contains("-Debug.zip") == true);

                var zipUrl = releaseAsset?["browser_download_url"]?.Value<string>() ?? "";
                var debugUrl = debugAsset?["browser_download_url"]?.Value<string>() ?? "";
                var size = releaseAsset?["size"]?.Value<long>() ?? 0;

                LatestVersion = tag;
                return (tag, htmlUrl, zipUrl, size);
            }
            catch
            {
                return ("0.0.0", "", "", 0);
            }
        }

        public async Task<string?> DownloadReleaseAsync(string zipUrl)
        {
            try
            {
                Directory.CreateDirectory(DownloadDir);
                var zipPath = Path.Combine(DownloadDir, "OpenSteamTool-Release.zip");

                var bytes = await _http.GetByteArrayAsync(zipUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);
                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> InstallToSteamAsync(string zipPath)
        {
            try
            {
                var steamPath = GetSteamPath();
                if (steamPath == null) return false;

                using var zip = ZipFile.OpenRead(zipPath);
                var needed = new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" };

                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileName(entry.Name);
                    if (!needed.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                    var dest = Path.Combine(steamPath, name);
                    entry.ExtractToFile(dest, overwrite: true);
                }

                // Save version file
                if (LatestVersion != null)
                {
                    var verPath = Path.Combine(steamPath, "opensteamtool.version");
                    await File.WriteAllTextAsync(verPath, LatestVersion);
                    CurrentVersion = LatestVersion;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DownloadAndInstallAsync()
        {
            try
            {
                var (_, _, zipUrl, _) = await CheckLatestReleaseAsync();
                if (string.IsNullOrEmpty(zipUrl)) return false;

                var zipPath = await DownloadReleaseAsync(zipUrl);
                if (zipPath == null) return false;

                return await InstallToSteamAsync(zipPath);
            }
            catch
            {
                return false;
            }
        }

        public bool IsInstalled()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return false;
            var needed = new[] { "dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll" };
            return needed.All(f => File.Exists(Path.Combine(steamPath, f)));
        }

        public string? GetInstalledVersion()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return null;
            var verFile = Path.Combine(steamPath, "opensteamtool.version");
            if (File.Exists(verFile))
            {
                CurrentVersion = File.ReadAllText(verFile).Trim();
                return CurrentVersion;
            }
            return null;
        }

        public string? GetSteamPath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\WOW6432Node\Valve\Steam") ??
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                    return path;
            }
            catch { }

            var defaultPath = @"C:\Program Files (x86)\Steam";
            return Directory.Exists(defaultPath) ? defaultPath : null;
        }

        public string GetLuaDir()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return "";
            var dir = Path.Combine(steamPath, "config", "lua");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public List<LuaConfigFile> GetLuaConfigs()
        {
            var luaDir = GetLuaDir();
            if (string.IsNullOrEmpty(luaDir) || !Directory.Exists(luaDir))
                return new();

            return Directory.GetFiles(luaDir, "*.lua")
                .Select(f => new LuaConfigFile
                {
                    FileName = Path.GetFileName(f),
                    Path = f,
                    Content = File.ReadAllText(f),
                    LastModified = File.GetLastWriteTime(f)
                })
                .OrderBy(f => f.FileName)
                .ToList();
        }

        public async Task SaveLuaConfigAsync(string fileName, string content)
        {
            var luaDir = GetLuaDir();
            if (string.IsNullOrEmpty(luaDir)) return;
            var path = Path.Combine(luaDir, fileName);
            await File.WriteAllTextAsync(path, content);
        }

        public void DeleteLuaConfig(string fileName)
        {
            var luaDir = GetLuaDir();
            if (string.IsNullOrEmpty(luaDir)) return;
            var path = Path.Combine(luaDir, fileName);
            if (File.Exists(path)) File.Delete(path);
        }

        public string GenerateLuaContent(string appId, string? depotKey = null,
            string? accessToken = null, string? manifestId = null,
            string? appTicket = null, string? eTicket = null,
            string? steamId = null)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(depotKey))
                lines.Add($"addappid({appId}, 0, \"{depotKey}\")");
            else
                lines.Add($"addappid({appId})");

            if (!string.IsNullOrEmpty(accessToken))
                lines.Add($"addtoken({appId},\"{accessToken}\")");

            if (!string.IsNullOrEmpty(manifestId))
                lines.Add($"setManifestid({appId},\"{manifestId}\")");

            if (!string.IsNullOrEmpty(appTicket))
                lines.Add($"setAppTicket({appId},\"{appTicket}\")");

            if (!string.IsNullOrEmpty(eTicket))
                lines.Add($"setETicket({appId},\"{eTicket}\")");

            if (!string.IsNullOrEmpty(steamId))
                lines.Add($"setStat({appId}, \"{steamId}\")");

            return string.Join("\n", lines) + "\n";
        }

        public async Task<bool> SaveTomlConfigAsync(string content)
        {
            try
            {
                var steamPath = GetSteamPath();
                if (steamPath == null) return false;
                var tomlPath = Path.Combine(steamPath, DefaultTomlPath);
                await File.WriteAllTextAsync(tomlPath, content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string? ReadTomlConfig()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return null;
            var tomlPath = Path.Combine(steamPath, DefaultTomlPath);
            return File.Exists(tomlPath) ? File.ReadAllText(tomlPath) : null;
        }

        public async Task<string?> FetchManifestCodeAsync(string gid)
        {
            try
            {
                var url = $"https://manifest.opensteamtool.com/{gid}";
                var resp = await _http.GetStringAsync(url);
                return resp?.Trim();
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> InstallLuaManifestAsync(string appId, string? depotKey = null, string? accessToken = null)
        {
            try
            {
                var luaDir = GetLuaDir();
                if (string.IsNullOrEmpty(luaDir)) return false;

                var content = GenerateLuaContent(appId, depotKey, accessToken);
                var fileName = $"manifest_{appId}.lua";
                await SaveLuaConfigAsync(fileName, content);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<SteamDepotInfo?> FetchDepotInfoAsync(string appId)
        {
            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us";
                var json = await _http.GetStringAsync(url);
                var data = JObject.Parse(json);
                var appData = data[appId]?["data"];
                if (appData == null) return null;

                var name = appData["name"]?.Value<string>() ?? "Unknown";
                var depots = appData["depots"] as JObject;

                var result = new SteamDepotInfo { Name = name, AppId = appId };

                if (depots != null)
                {
                    foreach (var prop in depots.Properties())
                    {
                        if (prop.Value is JObject depot && long.TryParse(prop.Name, out var depotId))
                        {
                            var gid = depot["manifest"]?.Value<string>();
                            if (!string.IsNullOrEmpty(gid))
                            {
                                result.Depots.Add(new SteamDepotEntry
                                {
                                    DepotId = depotId.ToString(),
                                    ManifestGid = gid,
                                    Name = depot["name"]?.Value<string>() ?? ""
                                });
                            }
                        }
                    }
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public string GetDefaultTomlContent()
        {
            return @"[log]
level = ""info""

[manifest]
url = ""opensteamtool""
timeout_resolve_ms = 5000
timeout_connect_ms = 5000
timeout_send_ms    = 10000
timeout_recv_ms    = 10000

[stats]
enable_api = true

[lua]
# paths = [""""]

[inject]
enabled = false
# library_x64 = ""OpenSteamTool.GameHook.x64.dll""
# library_x86 = ""OpenSteamTool.GameHook.x86.dll""

[cloud]
enabled = false
";
        }

        public List<OpenSteamToolLog> GetLogFiles()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return new();
            var logDir = Path.Combine(steamPath, "opensteamtool");
            if (!Directory.Exists(logDir)) return new();

            return Directory.GetFiles(logDir, "*.log")
                .Select(f => new OpenSteamToolLog
                {
                    FileName = Path.GetFileName(f),
                    Path = f,
                    Size = new FileInfo(f).Length,
                    LastModified = File.GetLastWriteTime(f)
                })
                .OrderBy(f => f.FileName)
                .ToList();
        }

        public string ReadLogFile(string logFilePath, int maxLines = 500)
        {
            try
            {
                var lines = File.ReadAllLines(logFilePath);
                if (lines.Length <= maxLines) return string.Join("\n", lines);
                return string.Join("\n", lines.Skip(lines.Length - maxLines));
            }
            catch
            {
                return "(error reading log)";
            }
        }

        public async Task ClearPatternCacheAsync()
        {
            var steamPath = GetSteamPath();
            if (steamPath == null) return;
            var cacheDir = Path.Combine(steamPath, "opensteamtool", "pattern");
            if (Directory.Exists(cacheDir))
            {
                await Task.Run(() => Directory.Delete(cacheDir, recursive: true));
            }
        }

        public bool HasUpdateAvailable()
        {
            if (LatestVersion == null || CurrentVersion == null) return false;
            var latest = ParseVersion(LatestVersion);
            var current = ParseVersion(CurrentVersion);
            if (latest == null || current == null) return false;
            return latest > current;
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

    public class LuaConfigFile
    {
        public string FileName { get; set; } = "";
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime LastModified { get; set; }
    }

    public class SteamDepotInfo
    {
        public string AppId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<SteamDepotEntry> Depots { get; set; } = new();
    }

    public class SteamDepotEntry
    {
        public string DepotId { get; set; } = "";
        public string ManifestGid { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class OpenSteamToolLog
    {
        public string FileName { get; set; } = "";
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string SizeDisplay => Size switch
        {
            < 1024 => $"{Size} B",
            < 1048576 => $"{Size / 1024.0:F1} KB",
            _ => $"{Size / 1048576.0:F1} MB"
        };
    }
}
