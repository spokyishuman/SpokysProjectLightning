using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SpokysProjectLightning.Services
{
    public class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool Mandatory { get; set; }
    }

    public class UpdateService
    {
        private readonly HttpClient _http;
        public static string UpdateCheckUrl { get; set; } = "https://api.github.com/repos/spokyishuman/SpokysProjectLightning/releases/latest";

        private static readonly string UpdateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysProjectLightning", "Updates");

        public UpdateService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL-UpdateChecker/4.0");
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        public static Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 2, 0, 0);

        public async Task<UpdateManifest?> CheckForUpdatesAsync(string? customUrl = null)
        {
            try
            {
                var url = customUrl ?? UpdateCheckUrl;
                var json = await _http.GetStringAsync(url);

                // GitHub API returns an array in "assets" - try to parse as our manifest format first
                // If it's a GitHub release, extract from the standard format
                try
                {
                    var gh = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    if (gh != null && !string.IsNullOrEmpty(gh.TagName))
                    {
                        var verStr = gh.TagName.TrimStart('v', 'V');
                        if (Version.TryParse(verStr, out var ghVer) && ghVer > CurrentVersion)
                        {
                            var asset = gh.Assets?.FirstOrDefault(a =>
                                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true ||
                                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
                            return new UpdateManifest
                            {
                                Version = ghVer.ToString(),
                                DownloadUrl = asset?.BrowserDownloadUrl ?? gh.ZipballUrl ?? "",
                                ReleaseNotes = gh.Body ?? "",
                                Mandatory = false
                            };
                        }
                        return null;
                    }
                }
                catch { }

                // Fallback: try direct manifest format
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Version))
                {
                    if (Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var mv) && mv > CurrentVersion)
                        return manifest;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<double>? progress = null)
        {
            try
            {
                Directory.CreateDirectory(UpdateDir);
                var zipPath = Path.Combine(UpdateDir, $"update-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

                using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var totalRead = 0L;
                var buffer = new byte[81920];

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                while (true)
                {
                    var bytesRead = await contentStream.ReadAsync(buffer);
                    if (bytesRead == 0) break;
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                        progress?.Report((double)totalRead / totalBytes * 100);
                }

                return zipPath;
            }
            catch
            {
                return null;
            }
        }

        public bool InstallUpdate(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath)) return false;

                var appDir = AppContext.BaseDirectory;
                var updaterPath = Path.Combine(UpdateDir, "SpokysProjectLightning.Updater.exe");

                // Copy the updater to a temp location if it doesn't exist
                var embeddedUpdater = Path.Combine(appDir, "SpokysProjectLightning.Updater.exe");
                if (File.Exists(embeddedUpdater))
                    File.Copy(embeddedUpdater, updaterPath, true);

                if (!File.Exists(updaterPath))
                {
                    // No updater exe - do it inline via PowerShell
                    return InstallUpdateViaPowerShell(zipPath, appDir);
                }

                var currentExe = Path.Combine(appDir, "SpokysProjectLightning.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{zipPath}\" \"{appDir.TrimEnd('\\')}\" \"{currentExe}\" {Environment.ProcessId}",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool InstallUpdateViaPowerShell(string zipPath, string appDir)
        {
            try
            {
                var script = $@"
Start-Sleep -Seconds 2
try {{
    Add-Type -A 'System.IO.Compression.FileSystem'
    $zip = [IO.Compression.ZipFile]::OpenRead('{zipPath}')
    foreach ($entry in $zip.Entries) {{
        $dest = Join-Path '{appDir}' $entry.FullName
        $dir = [IO.Path]::GetDirectoryName($dest)
        if (!(Test-Path $dir)) {{ New-Item -ItemType Directory -Path $dir -Force | Out-Null }}
        if (!$entry.Name) {{ continue }}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
    }}
    $zip.Dispose()
    Start-Process '{Path.Combine(appDir, "SpokysProjectLightning.exe")}'
}} catch {{
    [Console]::WriteLine($_.Exception.Message)
    Start-Sleep 5
}}
";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void CleanupOldUpdates()
        {
            try
            {
                if (Directory.Exists(UpdateDir))
                {
                    foreach (var f in Directory.GetFiles(UpdateDir, "update-*.zip"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }

        private class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }
            [JsonProperty("body")]
            public string? Body { get; set; }
            [JsonProperty("zipball_url")]
            public string? ZipballUrl { get; set; }
            [JsonProperty("assets")]
            public List<GitHubAsset>? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonProperty("name")]
            public string? Name { get; set; }
            [JsonProperty("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
