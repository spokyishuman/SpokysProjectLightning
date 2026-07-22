using System.IO.Compression;
using System.Diagnostics;

namespace SpokysProjectVercel.Updater;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: SpokysProjectVercel.Updater <zipPath> <targetDir> <restartExe> [parentPid]");
            return 1;
        }

        var zipPath = args[0];
        var targetDir = args[1];
        var restartExe = args[2];
        var parentPid = args.Length > 3 && int.TryParse(args[3], out var pid) ? pid : 0;

        Console.WriteLine($"Updater: {zipPath} -> {targetDir}");
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
        Thread.Sleep(1000);

        // Extract the update zip
        if (!File.Exists(zipPath))
        {
            Console.Error.WriteLine($"Update zip not found: {zipPath}");
            return 2;
        }

        try
        {
            Directory.CreateDirectory(targetDir);
            using var archive = ZipFile.OpenRead(zipPath);
            int count = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Path.Combine(targetDir, entry.FullName);
                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                entry.ExtractToFile(dest, overwrite: true);
                count++;
            }
            Console.WriteLine($"Extracted {count} files");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extract failed: {ex.Message}");
            return 3;
        }

        // Clean up the zip
        try { File.Delete(zipPath); } catch { }

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
