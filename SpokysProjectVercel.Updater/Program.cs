using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace SpokysProjectVercel.Updater;

class Program
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    static async Task<int> Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: SpokysProjectVercel.Updater <installerUrl> <targetDir> <restartExe> [parentPid]");
            return 1;
        }

        var installerUrl = args[0];
        var targetDir = args[1];
        var restartExe = args[2];
        var parentPid = args.Length > 3 && int.TryParse(args[3], out var pid) ? pid : 0;

        Console.WriteLine($"Updater: Downloading installer from {installerUrl}");
        Console.WriteLine($"Target: {targetDir}");
        Console.WriteLine($"Restart: {restartExe}");
        if (parentPid > 0)
            Console.WriteLine($"Waiting for PID {parentPid} to exit...");

        // Wait for the parent process to exit
        if (parentPid > 0)
        {
            try
            {
                var parent = Process.GetProcessById(parentPid);
                parent.WaitForExit(30000);
            }
            catch { }
        }

        // Extra wait to ensure files are unlocked
        await Task.Delay(1000);

        try
        {
            Directory.CreateDirectory(targetDir);

            // Download installer
            var installerPath = Path.Combine(targetDir, "setup.exe");
            Console.WriteLine($"Downloading installer to {installerPath}...");
            
            using (var response = await HttpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var totalRead = 0L;
                
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;
                    if (totalBytes > 0)
                    {
                        var progress = (double)totalRead / totalBytes * 100;
                        Console.Write($"\rProgress: {progress:F1}%");
                    }
                }
            }
            Console.WriteLine($"\nDownload complete: {installerPath}");

            // Run installer silently
            Console.WriteLine("Running installer...");
            var installerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                UseShellExecute = false,
                WorkingDirectory = targetDir,
                CreateNoWindow = true
            });
            
            if (installerProcess != null)
            {
                await installerProcess.WaitForExitAsync();
                Console.WriteLine($"Installer exited with code: {installerProcess.ExitCode}");
            }

            // Clean up installer
            try { File.Delete(installerPath); } catch { }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Update failed: {ex.Message}");
            return 3;
        }

        // Restart the app
        if (!string.IsNullOrEmpty(restartExe) && File.Exists(restartExe))
        {
            Console.WriteLine($"Starting: {restartExe}");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = restartExe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(restartExe)
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Restart failed: {ex.Message}");
                return 4;
            }
        }

        return 0;
    }
}