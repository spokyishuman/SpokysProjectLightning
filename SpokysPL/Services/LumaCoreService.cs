using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SpokysProjectVercel.Services
{
    public class LumaCoreService
    {
        private const string LUMACORE_DOWNLOAD_URL =
            "https://github.com/KoriaPolis/LumaCore/releases/download/V19/Release.zip";

        private static readonly string[] LumaCoreDlls = {
            "dwmapi.dll", "xinput1_4.dll", "LumaCore.dll", "LumaCorePayload.dll"
        };

        private readonly HttpClient _http;

        public LumaCoreService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL-LumaCore/4.0");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public static bool IsLumaCoreInstalled()
        {
            var steamPath = SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return false;
            return File.Exists(Path.Combine(steamPath, "LumaCore.dll"));
        }

        public async Task<bool> InstallIfMissingAsync()
        {
            if (IsLumaCoreInstalled()) return false;

            var steamPath = SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath)) return false;

            try
            {
                var zipBytes = await _http.GetByteArrayAsync(LUMACORE_DOWNLOAD_URL);
                if (zipBytes == null || zipBytes.Length == 0) return false;

                int extracted = 0;
                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

                foreach (var entry in zip.Entries)
                {
                    var fileName = Path.GetFileName(entry.Name);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (!LumaCoreDlls.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;

                    var destPath = Path.Combine(steamPath, fileName);
                    entry.ExtractToFile(destPath, overwrite: true);
                    extracted++;
                }

                return extracted > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> InstallAsync()
        {
            var steamPath = SteamService.FindSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                throw new InvalidOperationException("Steam installation not found.");

            var zipBytes = await _http.GetByteArrayAsync(LUMACORE_DOWNLOAD_URL);
            if (zipBytes == null || zipBytes.Length == 0)
                throw new InvalidOperationException("Failed to download LumaCore release.");

            int extracted = 0;
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            foreach (var entry in zip.Entries)
            {
                var fileName = Path.GetFileName(entry.Name);
                if (string.IsNullOrEmpty(fileName)) continue;
                if (!LumaCoreDlls.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;

                var destPath = Path.Combine(steamPath, fileName);
                entry.ExtractToFile(destPath, overwrite: true);
                extracted++;
            }

            if (extracted == 0)
                throw new InvalidOperationException("No LumaCore DLLs found in the downloaded archive.");

            return extracted;
        }
    }
}
