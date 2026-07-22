using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SpokysProjectVercel.Models;

namespace SpokysProjectVercel.Services
{
    /// <summary>
    /// Downloads an online-fix file, extracts it (RAR/ZIP/7z/split .001), and copies the
    /// fix contents into a game's install directory — the "Add Fix" flow.
    /// Extraction order: 7-Zip → WinRAR → SharpCompress (in-process) → System.IO.Compression (plain ZIP).
    /// 7-Zip/WinRAR are detected via the Windows registry first, then common install folders.
    /// </summary>
    public class FixInstallerService
    {
        private readonly HttpClient _http;

        public static string DefaultFixesRoot => Path.Combine(DataService.GetDownloadPath(), "OnlineFixes");

        public FixInstallerService()
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
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept", "*/*");
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _http.DefaultRequestHeaders.Add("Referer", "https://online-fix.me");
            _http.Timeout = TimeSpan.FromMinutes(30);
            Directory.CreateDirectory(DefaultFixesRoot);
        }

        public class InstallProgress
        {
            public string Status { get; set; } = "";
            public int Percent { get; set; }
            public long BytesReceived { get; set; }
            public long TotalBytes { get; set; }
        }

        public class FixInstallResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string DownloadPath { get; set; } = "";
            public string ExtractedPath { get; set; } = "";
            public string InstalledTo { get; set; } = "";
            public List<string> Files { get; set; } = new();
        }

        /// <summary>Human-readable status of which extractor is available (for UI warnings).</summary>
        public string GetExtractorStatus()
        {
            var seven = Find7Zip();
            if (seven != null) return $"7-Zip ({seven})";
            var winRar = FindWinRar();
            if (winRar != null) return $"WinRAR ({winRar})";
            return "Built-in (SharpCompress) — install 7-Zip for best results with password/split archives";
        }

        /// <summary>
        /// Full flow: download the fix file → extract → (optionally) copy into a
        /// game directory. Reports download progress. On failure, partial downloads are cleaned up.
        /// </summary>
        public async Task<FixInstallResult> DownloadExtractInstallAsync(
            string url, string gameName, string? gameDir = null,
            string password = "online-fix.me",
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancel = default)
        {
            var result = new FixInstallResult();
            string? downloadPath = null;
            try
            {
                // 1. Download
                progress?.Report(new InstallProgress { Status = "Connecting...", Percent = 0 });

                var fileName = GuessFileName(url, gameName);
                var gameFolder = Path.Combine(DefaultFixesRoot, Sanitize(gameName));
                Directory.CreateDirectory(gameFolder);
                downloadPath = Path.Combine(gameFolder, fileName);

                var totalBytes = await DownloadWithProgressAsync(url, downloadPath, progress, cancel);
                result.DownloadPath = downloadPath;

                // 2. Extract
                progress?.Report(new InstallProgress { Status = "Extracting archive...", Percent = 100 });
                var extractDir = Path.Combine(gameFolder, "extracted");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);

                bool extracted = await ExtractArchiveAsync(downloadPath, extractDir, password, progress);
                if (!extracted)
                {
                    // Clean up the unhelpful partial file so it is re-downloaded next time
                    TryDelete(downloadPath);
                    result.Success = false;
                    result.Message = $"Downloaded {FormatSize(totalBytes)} but could not auto-extract it.\n" +
                                     $"{GetExtractorStatus()}\n" +
                                     $"Open this folder and extract manually: {gameFolder}";
                    result.ExtractedPath = downloadPath;
                    return result;
                }

                result.ExtractedPath = extractDir;
                var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).ToList();
                result.Files = extractedFiles.Select(f => Path.GetRelativePath(extractDir, f)).ToList();

                // 3. Resolve / use the game directory
                var targetDir = gameDir;
                if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                    targetDir = TryFindGameInstallDir(gameName);

                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    progress?.Report(new InstallProgress { Status = $"Installing into {targetDir}...", Percent = 100 });
                    CopyAll(extractDir, targetDir);
                    result.InstalledTo = targetDir;
                    result.Success = true;
                    result.Message = $"Fix installed into game folder.\n{extractedFiles.Count} files copied to {targetDir}";
                }
                else
                {
                    result.Success = true;
                    result.Message = $"Fix downloaded and extracted.\n" +
                                     $"No game folder was detected. Copy the fix files into your game directory:\n{extractDir}";
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                TryDelete(downloadPath);
                result.Success = false;
                result.Message = "Download cancelled.";
                return result;
            }
            catch (Exception ex)
            {
                TryDelete(downloadPath);
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Try every available extractor in order: 7-Zip → WinRAR → SharpCompress → (ZIP only) .NET.
        /// Returns true if the archive was extracted.
        /// </summary>
        private async Task<bool> ExtractArchiveAsync(string archive, string destDir, string password, IProgress<InstallProgress>? progress)
        {
            var sevenZip = Find7Zip();
            if (sevenZip != null)
            {
                progress?.Report(new InstallProgress { Status = "Extracting with 7-Zip...", Percent = 100 });
                if (await ExtractWithExternalToolAsync(sevenZip, archive, destDir, password))
                    return true;
            }

            var winRar = FindWinRar();
            if (winRar != null)
            {
                progress?.Report(new InstallProgress { Status = "Extracting with WinRAR...", Percent = 100 });
                if (await ExtractWithExternalToolAsync(winRar, archive, destDir, password))
                    return true;
            }

            // In-process fallback (handles RAR/7z/ZIP, including split archives).
            try
            {
                progress?.Report(new InstallProgress { Status = "Extracting (built-in)...", Percent = 100 });
                if (ExtractWithSharpCompress(archive, destDir, password))
                    return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpCompress failed: {ex.Message}");
            }

            // Last resort: plain ZIP via System.IO.Compression
            if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ZipFile.ExtractToDirectory(archive, destDir, true);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ZipFile failed: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Extract RAR/7z/ZIP/split (.001) using an external tool (7-Zip or WinRAR).
        /// Both tools natively handle password-protected and split (.001) archives.
        /// </summary>
        private async Task<bool> ExtractWithExternalToolAsync(string toolPath, string archive, string destDir, string password)
        {
            bool is7z = toolPath.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase);
            string args;
            if (is7z)
            {
                // 7-Zip: -x=5 balanced, -aos skip existing, -p password, -y yes to prompts
                args = $"x \"{archive}\" -o\"{destDir}\" -aos -y" + (string.IsNullOrEmpty(password) ? "" : $" -p\"{password}\"");
            }
            else
            {
                args = $"x \"{archive}\" \"{destDir}\\\" -y" + (string.IsNullOrEmpty(password) ? "" : $" -p\"{password}\"");
            }
            return await RunProcessAsync(toolPath, args, destDir);
        }

        /// <summary>In-process extraction via SharpCompress (RAR/7z/ZIP, split archives supported).
        /// Used as a fallback when 7-Zip/WinRAR are not installed. Handles password-protected archives.</summary>
        private static bool ExtractWithSharpCompress(string archive, string destDir, string password)
        {
            var options = new ReaderOptions();
            if (!string.IsNullOrEmpty(password)) options.Password = password;

            using var arc = ArchiveFactory.OpenArchive(archive, options);
            foreach (var entry in arc.Entries)
            {
                if (entry.IsDirectory) continue;
                try
                {
                    entry.WriteToDirectory(destDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpCompress entry {entry.Key}: {ex.Message}");
                }
            }
            return Directory.GetFiles(destDir, "*", SearchOption.AllDirectories).Length > 0;
        }

        /// <summary>
        /// Install an already-downloaded archive file: extract it (7-Zip/WinRAR/SharpCompress)
        /// and copy the fix files into the detected/provided game directory. Used by drag-drop.
        /// </summary>
        public async Task<FixInstallResult> InstallLocalArchiveAsync(
            string archivePath, string gameName, string? gameDir = null,
            string password = "online-fix.me",
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancel = default)
        {
            var result = new FixInstallResult();
            try
            {
                if (!File.Exists(archivePath))
                {
                    result.Success = false;
                    result.Message = $"File not found: {archivePath}";
                    return result;
                }

                progress?.Report(new InstallProgress { Status = "Extracting archive...", Percent = 0 });
                var gameFolder = Path.Combine(DefaultFixesRoot, Sanitize(gameName));
                Directory.CreateDirectory(gameFolder);
                var extractDir = Path.Combine(gameFolder, "extracted");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);

                bool extracted = await ExtractArchiveAsync(archivePath, extractDir, password, progress);
                result.DownloadPath = archivePath;
                result.ExtractedPath = extractDir;
                if (!extracted)
                {
                    result.Success = false;
                    result.Message = $"Could not auto-extract {Path.GetFileName(archivePath)}.\n{GetExtractorStatus()}\n" +
                                     $"Open this folder and extract manually: {gameFolder}";
                    return result;
                }

                var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).ToList();
                result.Files = extractedFiles.Select(f => Path.GetRelativePath(extractDir, f)).ToList();

                var targetDir = gameDir;
                if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                    targetDir = TryFindGameInstallDir(gameName);

                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    progress?.Report(new InstallProgress { Status = $"Installing into {targetDir}...", Percent = 100 });
                    CopyAll(extractDir, targetDir);
                    result.InstalledTo = targetDir;
                    result.Success = true;
                    result.Message = $"Fix installed into game folder.\n{extractedFiles.Count} files copied to {targetDir}";
                }
                else
                {
                    result.Success = true;
                    result.Message = $"Fix extracted.\nNo game folder was detected. Copy the fix files into your game directory:\n{extractDir}";
                }
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
                return result;
            }
        }

        // ========== TOOL DETECTION (registry first, then common paths) ==========

        private static string? Find7Zip()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\7-Zip") ??
                                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\7-Zip");
                var path = key?.GetValue("Path") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    var exe = Path.Combine(path, "7z.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            var candidates = new[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "7-Zip", "7z.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? FindWinRar()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinRAR") ??
                                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinRAR");
                var path = key?.GetValue("exe32") as string ?? key?.GetValue("exe64") as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            catch { }

            var candidates = new[]
            {
                @"C:\Program Files\WinRAR\WinRAR.exe",
                @"C:\Program Files\WinRAR\UnRAR.exe",
                @"C:\Program Files (x86)\WinRAR\WinRAR.exe",
                @"C:\Program Files (x86)\WinRAR\UnRAR.exe"
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Best-effort: locate the game's install folder via Steam library VDFs.</summary>
        public static string? TryFindGameInstallDir(string gameName)
        {
            try
            {
                var steamPath = SteamService.FindSteamPath();
                if (string.IsNullOrEmpty(steamPath)) return null;
                var wanted = Sanitize(gameName).ToLowerInvariant();

                foreach (var lib in SteamService.GetSteamLibraryFolders())
                {
                    var common = Path.Combine(lib, "common");
                    if (!Directory.Exists(common)) continue;
                    foreach (var dir in Directory.GetDirectories(common))
                    {
                        var folder = Sanitize(Path.GetFileName(dir)).ToLowerInvariant();
                        if (folder == wanted || folder.Contains(wanted) || wanted.Contains(folder))
                            return dir;
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<bool> RunProcessAsync(string exe, string args, string workDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                await p.WaitForExitAsync();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void CopyAll(string sourceDir, string targetDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }

        private static string GuessFileName(string url, string gameName)
        {
            try
            {
                var uri = new Uri(url);
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(name) && name.Contains("."))
                    return Sanitize(name);
            }
            catch { }
            return Sanitize(gameName) + ".rar";
        }

        private static string Sanitize(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in s) sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString().Trim();
        }

        private static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double s = bytes; int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:F1} {u[i]}";
        }

        private async Task<long> DownloadWithProgressAsync(string url, string destPath,
            IProgress<InstallProgress>? progress, CancellationToken cancel)
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;
            long received = 0;
            var buffer = new byte[81920];
            var sw = Stopwatch.StartNew();
            long lastReport = 0;

            using var content = await response.Content.ReadAsStreamAsync(cancel);
            using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            while (true)
            {
                var read = await content.ReadAsync(buffer, cancel);
                if (read == 0) break;
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                received += read;

                if (progress != null && sw.ElapsedMilliseconds - lastReport > 300)
                {
                    var pct = total > 0 ? (int)(received * 100 / total) : 0;
                    var speed = received / (sw.ElapsedMilliseconds / 1000.0);
                    progress.Report(new InstallProgress
                    {
                        Status = $"Downloading... {FormatSize(received)}/{(total > 0 ? FormatSize(total) : "?")} ({FormatSize((long)speed)}/s)",
                        Percent = pct,
                        BytesReceived = received,
                        TotalBytes = total
                    });
                    lastReport = sw.ElapsedMilliseconds;
                }
            }
            progress?.Report(new InstallProgress { Status = "Download complete.", Percent = 100, BytesReceived = received, TotalBytes = total });
            return received;
        }
    }
}

