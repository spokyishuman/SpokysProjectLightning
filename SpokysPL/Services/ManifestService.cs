using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    public class ManifestService
    {
        private readonly FaresService _fares;
        private readonly HttpClient _http;

        public class InstallResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int ManifestsInstalled { get; set; }
            public string AppManifestPath { get; set; } = "";
            public string LuaPath { get; set; } = "";
            public string DepotCachePath { get; set; } = "";
            public List<string> InstalledFiles { get; set; } = new();
            public long TotalBytes { get; set; }
        }

        private static HttpClient MakeClient()
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
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Accept", "*/*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }

        public ManifestService()
        {
            _fares = new FaresService();
            _http = MakeClient();
        }

        /// <summary>
        /// Full install: fetch manifest list → download each .manifest →
        /// place in depotcache → write .acf with InstalledDepots → write .lua.
        /// Reports progress per file.
        /// </summary>
        public async Task<InstallResult> InstallGameManifestAsync(
            string appId, string gameName, string? targetSteamPath = null,
            IProgress<(int done, int total, string file)>? progress = null)
        {
            var result = new InstallResult();
            try
            {
                var steamPath = targetSteamPath ?? SteamService.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                {
                    result.Success = false;
                    result.Message = "Steam not found. Install Steam first.";
                    return result;
                }

                // 1. Fetch manifest list from fares.top
                progress?.Report((0, 0, "Fetching manifest list from fares.top..."));
                var manifestData = await _fares.GetManifestsAsync(appId);
                if (!string.IsNullOrEmpty(manifestData.Error))
                {
                    result.Success = false;
                    result.Message = $"fares.top: {manifestData.Error}";
                    return result;
                }
                if (manifestData.Manifests.Count == 0)
                {
                    result.Success = false;
                    result.Message = "No manifests available for this App ID.";
                    return result;
                }

                // Use the game name from fares if we didn't pass one
                if (string.IsNullOrEmpty(gameName) || gameName == appId)
                    gameName = manifestData.Name;

                // Only download depots that actually have a manifest gid
                var downloadable = manifestData.Manifests.Where(m => !string.IsNullOrEmpty(m.ManifestId)).ToList();
                var total = downloadable.Count;

                ManifestPaths.EnsureDirs();

                var installedDepots = new List<(string depotId, string manifestId, long size)>();
                var installedFiles = new List<string>();
                long totalBytes = 0;
                int done = 0;

                // 3. Download each manifest file into SteamDaddy's data directory
                foreach (var m in downloadable)
                {
                    done++;
                    var fileName = $"{m.DepotId}_{m.ManifestId}.manifest";
                    var destPath = Path.Combine(ManifestPaths.ManifestDir, fileName);
                    progress?.Report((done, total, $"Downloading {fileName}..."));

                    try
                    {
                        var bytes = await DownloadManifestBytesAsync(appId, m.DepotId, m.ManifestId, 1);
                        if (bytes == null || bytes.Length == 0)
                            bytes = await DownloadManifestBytesAsync(appId, m.DepotId, m.ManifestId, 2);
                        if (bytes != null && bytes.Length > 0)
                        {
                            await File.WriteAllBytesAsync(destPath, bytes);
                            installedFiles.Add(destPath);
                            installedDepots.Add((m.DepotId, m.ManifestId, m.Size));
                            totalBytes += bytes.Length;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Primary server failed for {fileName}: {ex.Message}");
                        try
                        {
                            var bytes = await DownloadManifestBytesAsync(appId, m.DepotId, m.ManifestId, 2);
                            if (bytes != null && bytes.Length > 0)
                            {
                                await File.WriteAllBytesAsync(destPath, bytes);
                                installedFiles.Add(destPath);
                                installedDepots.Add((m.DepotId, m.ManifestId, m.Size));
                                totalBytes += bytes.Length;
                            }
                        }
                        catch (Exception inner)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed {fileName}: {inner.Message}");
                        }
                    }
                }

                if (installedDepots.Count == 0)
                {
                    result.Success = false;
                    result.Message = "All manifest downloads failed. Check your connection.";
                    return result;
                }

                // 4. Generate and write appmanifest_{appId}.acf WITH InstalledDepots
                var steamappsDir = Path.Combine(steamPath, "steamapps");
                Directory.CreateDirectory(steamappsDir);
                var acfPath = Path.Combine(steamappsDir, $"appmanifest_{appId}.acf");
                var acfContent = GenerateAppManifestAcf(appId, gameName, steamPath, installedDepots);
                await File.WriteAllTextAsync(acfPath, acfContent);
                installedFiles.Add(acfPath);
                result.AppManifestPath = acfPath;

                // 5. Write/update the .lua with setManifestid entries in SteamDaddy's data
                var luaPath = Path.Combine(ManifestPaths.LuaDir, $"{appId}.lua");
                await File.WriteAllTextAsync(luaPath, GenerateLuaList(appId, gameName, installedDepots));
                installedFiles.Add(luaPath);
                result.LuaPath = luaPath;

                // 6. Copy files directly to Steam directories (like SFF/LumaCore)
                try
                {
                    var depotCache = Path.Combine(steamPath, "depotcache");
                    var configDepot = Path.Combine(steamPath, "config", "depotcache");
                    var stplugIn = Path.Combine(steamPath, "config", "stplug-in");
                    Directory.CreateDirectory(depotCache);
                    Directory.CreateDirectory(configDepot);
                    Directory.CreateDirectory(stplugIn);

                    foreach (var m in downloadable)
                    {
                        var fileName = $"{m.DepotId}_{m.ManifestId}.manifest";
                        var src = Path.Combine(ManifestPaths.ManifestDir, fileName);
                        if (File.Exists(src))
                        {
                            var dst = Path.Combine(depotCache, fileName);
                            File.Copy(src, dst, true);
                            installedFiles.Add(dst);
                            var dst2 = Path.Combine(configDepot, fileName);
                            File.Copy(src, dst2, true);
                            installedFiles.Add(dst2);
                        }
                    }

                    var steamLuaDst = Path.Combine(stplugIn, $"{appId}.lua");
                    if (File.Exists(luaPath))
                    {
                        File.Copy(luaPath, steamLuaDst, true);
                        installedFiles.Add(steamLuaDst);
                    }

                    result.DepotCachePath = depotCache;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ManifestService] Copy to Steam dirs failed: {ex.Message}");
                }

                result.Success = true;
                result.ManifestsInstalled = installedDepots.Count;
                result.TotalBytes = totalBytes;
                result.InstalledFiles = installedFiles;
                result.Message = $"Installed {installedDepots.Count} manifests ({FormatSize(totalBytes)}) for {gameName}.\n" +
                                 $"Files placed in Steam's depotcache and lua directories.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Install failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>Download a single manifest's raw protobuf bytes from fares.top.</summary>
        private async Task<byte[]?> DownloadManifestBytesAsync(string appId, string depotId, string manifestId, int server = 1)
        {
            var url = $"{FaresService.Base}/api/manifest/download?depotId={Uri.EscapeDataString(depotId)}" +
                      $"&manifestId={Uri.EscapeDataString(manifestId)}&appId={Uri.EscapeDataString(appId)}";
            if (server == 2) url += "&server=2";
            return await _http.GetByteArrayAsync(url);
        }

        /// <summary>
        /// Generate a valid appmanifest_{appId}.acf — matches Steam's real format
        /// with the InstalledDepots block that SteamTools needs to mount the game.
        /// </summary>
        private string GenerateAppManifestAcf(string appId, string gameName, string steamPath,
            List<(string depotId, string manifestId, long size)> depots)
        {
            var launcherPath = Path.Combine(steamPath, "steam.exe");
            var installDir = SanitizeFolderName(gameName);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var sb = new StringBuilder();
            sb.AppendLine("\"AppState\"");
            sb.AppendLine("{");
            sb.AppendLine($"\t\"appid\"\t\t\"{appId}\"");
            sb.AppendLine("\t\"universe\"\t\t\"1\"");
            sb.AppendLine($"\t\"LauncherPath\"\t\t\"{launcherPath}\"");
            sb.AppendLine($"\t\"name\"\t\t\"{EscapeVdf(gameName)}\"");
            sb.AppendLine("\t\"StateFlags\"\t\t\"4\"");
            sb.AppendLine($"\t\"installdir\"\t\t\"{installDir}\"");
            sb.AppendLine($"\t\"LastUpdated\"\t\t\"{now}\"");
            sb.AppendLine("\t\"LastPlayed\"\t\t\"0\"");
            sb.AppendLine($"\t\"SizeOnDisk\"\t\t\"{depots.Sum(d => d.size)}\"");
            sb.AppendLine("\t\"StagingSize\"\t\t\"0\"");
            sb.AppendLine("\t\"buildid\"\t\t\"0\"");
            sb.AppendLine("\t\"LastOwner\"\t\t\"0\"");
            sb.AppendLine("\t\"UpdateResult\"\t\t\"0\"");
            sb.AppendLine("\t\"BytesToDownload\"\t\t\"0\"");
            sb.AppendLine("\t\"BytesDownloaded\"\t\t\"0\"");
            sb.AppendLine("\t\"AutoUpdateBehavior\"\t\t\"0\"");
            sb.AppendLine("\t\"AllowOtherDownloadsWhileRunning\"\t\t\"0\"");
            sb.AppendLine("\t\"ScheduledAutoUpdate\"\t\t\"0\"");
            sb.AppendLine("\t\"InstalledDepots\"");
            sb.AppendLine("\t{");
            foreach (var d in depots)
            {
                sb.AppendLine($"\t\t\"{d.depotId}\"");
                sb.AppendLine("\t\t{");
                sb.AppendLine($"\t\t\t\"manifest\"\t\t\"{d.manifestId}\"");
                sb.AppendLine($"\t\t\t\"size\"\t\t\"{d.size}\"");
                sb.AppendLine("\t\t}");
            }
            sb.AppendLine("\t}");
            sb.AppendLine("\t\"UserConfig\"");
            sb.AppendLine("\t{");
            sb.AppendLine("\t}");
            sb.AppendLine("\t\"MountedConfig\"");
            sb.AppendLine("\t{");
            sb.AppendLine("\t}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// SFF/LumaCore-style .lua generation: addappid for the base app and each depot,
        /// plus setManifestid for manifest pinning. LumaCore auto-registers the appid
        /// from the filename, but the depot addappid calls also register decryption keys
        /// and depot ownership.
        /// </summary>
        public string GenerateLuaList(string appId, string gameName,
            List<(string depotId, string manifestId, long size)> depots)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"-- MAIN APPLICATION");
            sb.AppendLine($"addappid({appId}) -- {EscapeVdf(gameName)}");
            sb.AppendLine();
            sb.AppendLine($"-- MAIN APP DEPOTS");
            foreach (var d in depots)
            {
                sb.AppendLine($"addappid({d.depotId}, 1) -- Depot {d.depotId}");
                sb.AppendLine($"setManifestid({d.depotId}, \"{d.manifestId}\")");
            }
            return sb.ToString();
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private static string EscapeVdf(string s) => s.Replace("\"", "\\\"");

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes; int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:F1} {units[i]}";
        }

        /// <summary>Check if a game already has an appmanifest installed.</summary>
        public bool IsGameInstalled(string appId, string? targetSteamPath = null)
        {
            var steamPath = targetSteamPath ?? SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return false;
            return File.Exists(Path.Combine(steamPath, "steamapps", $"appmanifest_{appId}.acf"));
        }

        /// <summary>Remove a game's manifest, acf, lua, and depot-cache files.</summary>
        public bool UninstallGameManifest(string appId, string? targetSteamPath = null)
        {
            var steamPath = targetSteamPath ?? SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return false;
            bool removed = false;

            var acf = Path.Combine(steamPath, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(acf)) { try { File.Delete(acf); removed = true; } catch { } }

            // Delete .lua from all possible mode directories
            foreach (var subDir in new[] { "lua", "stplug-in" })
            {
                var lua = Path.Combine(steamPath, "config", subDir, $"{appId}.lua");
                if (File.Exists(lua)) { try { File.Delete(lua); removed = true; } catch { } }
            }

            // Read the acf's InstalledDepots block so we only delete THIS app's depots
            var depotIds = new HashSet<string>(ReadInstalledDepotIds(acf));
            var depotCacheDir = Path.Combine(steamPath, "config", "depotcache");
            if (Directory.Exists(depotCacheDir))
            {
                foreach (var file in Directory.GetFiles(depotCacheDir, "*.manifest"))
                {
                    var name = Path.GetFileName(file);
                    if (depotIds.Count > 0 && depotIds.Any(id => name.StartsWith(id + "_")))
                    {
                        try { File.Delete(file); removed = true; } catch { }
                    }
                }
            }

            return removed;
        }

        /// <summary>Parse the depot IDs out of an appmanifest .acf's InstalledDepots block.</summary>
        private static List<string> ReadInstalledDepotIds(string acfPath)
        {
            var ids = new List<string>();
            if (!File.Exists(acfPath)) return ids;
            try
            {
                var content = File.ReadAllText(acfPath);
                var matches = Regex.Matches(content, @"""(\d+)""\s*\n\s*\{");
                foreach (Match m in matches) ids.Add(m.Groups[1].Value);
            }
            catch { }
            return ids;
        }

        /// <summary>List all installed games (those with appmanifest files).</summary>
        public List<GameInfo> GetInstalledGames(string? targetSteamPath = null)
        {
            var games = new List<GameInfo>();
            var steamPath = targetSteamPath ?? SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return games;

            var steamappsDir = Path.Combine(steamPath, "steamapps");
            if (!Directory.Exists(steamappsDir)) return games;

            foreach (var acf in Directory.GetFiles(steamappsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(acf);
                    var idMatch = Regex.Match(content, @"""appid""\s+""(\d+)""");
                    var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
                    if (idMatch.Success)
                    {
                        games.Add(new GameInfo
                        {
                            AppId = idMatch.Groups[1].Value,
                            Name = nameMatch.Success ? nameMatch.Groups[1].Value : "Unknown",
                            Category = "Installed",
                            IsActive = true
                        });
                    }
                }
                catch { }
            }
            return games;
        }
    }
}

