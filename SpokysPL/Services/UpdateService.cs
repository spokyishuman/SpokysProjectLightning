using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SpokysProjectVercel.Services
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
        public static string UpdateCheckUrl { get; set; } = "https://raw.githubusercontent.com/spokyishuman/SpokysProjectLightning/main/update.json";

        private static readonly string UpdateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpokysProjectVercel", "Updates");

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

        private static Version NormalizeVersion(Version v)
        {
            var maj = v.Major;
            var min = v.Minor < 0 ? 0 : v.Minor;
            var build = v.Build < 0 ? 0 : v.Build;
            var rev = v.Revision < 0 ? 0 : v.Revision;
            return new Version(maj, min, build, rev);
        }

        public async Task<UpdateManifest?> CheckForUpdatesAsync(string? customUrl = null)
        {
            try
            {
                var url = customUrl ?? UpdateCheckUrl;
                var json = await _http.GetStringAsync(url);

                // GitHub API returns an array in "assets" - try to parse as our manifest format first
                try
                {
                    var gh = JsonConvert.DeserializeObject<GitHubRelease>(json);
                    if (gh != null && !string.IsNullOrEmpty(gh.TagName))
                    {
                        var verStr = gh.TagName.TrimStart('v', 'V');
                        if (Version.TryParse(verStr, out var rawVer))
                        {
                            var ghVer = NormalizeVersion(rawVer);
                            if (ghVer > CurrentVersion)
                            {
                                var asset = gh.Assets?
                                    .OrderByDescending(a =>
                                        a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ? 1 :
                                        a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true ? 0 : -1)
                                    .FirstOrDefault(a =>
                                        a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ||
                                        a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
                                return new UpdateManifest
                                {
                                    Version = ghVer.ToString(),
                                    DownloadUrl = asset?.BrowserDownloadUrl ?? gh.ZipballUrl ?? "",
                                    ReleaseNotes = gh.Body ?? "",
                                    Mandatory = false
                                };
                            }
                        }
                        return null;
                    }
                }
                catch { }

                // Fallback: try direct manifest format
                var manifest = JsonConvert.DeserializeObject<UpdateManifest>(json);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Version))
                {
                    if (Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var rawMv))
                    {
                        var mv = NormalizeVersion(rawMv);
                        if (mv > CurrentVersion)
                            return manifest;
                    }
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
            Directory.CreateDirectory(UpdateDir);
            var ext = Path.GetExtension(new Uri(downloadUrl).AbsolutePath) ?? ".zip";
            var filePath = Path.Combine(UpdateDir, $"update-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var totalRead = 0L;
            var buffer = new byte[81920];

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            while (true)
            {
                var bytesRead = await contentStream.ReadAsync(buffer);
                if (bytesRead == 0) break;
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)totalRead / totalBytes * 100);
            }

            return filePath;
        }

        public bool InstallUpdate(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // If it's an installer .exe, run it silently
                if (ext == ".exe")
                {
                    var appDir = AppContext.BaseDirectory;
                    var currentExe = Path.Combine(appDir, "SpokysProjectVercel.exe");
                    var psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=\"{appDir.TrimEnd('\\')}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    return proc != null;
                }

                // ZIP-based update
                var appDir2 = AppContext.BaseDirectory;
                var updaterPath = Path.Combine(UpdateDir, "SpokysProjectVercel.Updater.exe");

                var embeddedUpdater = Path.Combine(appDir2, "SpokysProjectVercel.Updater.exe");
                if (File.Exists(embeddedUpdater))
                    File.Copy(embeddedUpdater, updaterPath, true);

                if (!File.Exists(updaterPath))
                    return InstallUpdateViaPowerShell(filePath, appDir2);

                var currentExe2 = Path.Combine(appDir2, "SpokysProjectVercel.exe");
                var psi2 = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{filePath}\" \"{appDir2.TrimEnd('\\')}\" \"{currentExe2}\" {Environment.ProcessId}",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                Process.Start(psi2);
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
                var safeZip = zipPath.Replace("'", "''");
                var safeDir = appDir.Replace("'", "''");
                var safeExe = Path.Combine(appDir, "SpokysProjectVercel.exe").Replace("'", "''");
                var script = $@"
Start-Sleep -Seconds 2
try {{
    Add-Type -A 'System.IO.Compression.FileSystem'
    $zip = [IO.Compression.ZipFile]::OpenRead('{safeZip}')
    foreach ($entry in $zip.Entries) {{
        $dest = Join-Path '{safeDir}' $entry.FullName
        $dir = [IO.Path]::GetDirectoryName($dest)
        if (!(Test-Path $dir)) {{ New-Item -ItemType Directory -Path $dir -Force | Out-Null }}
        if (!$entry.Name) {{ continue }}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
    }}
    $zip.Dispose()
    Start-Process '{safeExe}'
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
                    foreach (var f in Directory.GetFiles(UpdateDir, "update-*"))
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
