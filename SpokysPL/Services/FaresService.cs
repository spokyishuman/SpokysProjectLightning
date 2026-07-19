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
    /// <summary>
    /// fares.top manifest API client — ported from the proven Discord bot (fares.ts).
    /// Fetches Steam depot manifests and bundles them into a zip for the bypass page.
    /// </summary>
    public class FaresService
    {
        public const string Base = "https://fares.top";
        private const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";

        private readonly HttpClient _http;
        private List<CatalogEntry> _catalog = new();
        private bool _catalogLoaded;

        public FaresService()
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
            _http.DefaultRequestHeaders.Add("User-Agent", UA);
            _http.DefaultRequestHeaders.Add("Accept", "*/*");
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public class CatalogEntry
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public class ManifestFile
        {
            public string DepotId { get; set; } = "";
            public string ManifestId { get; set; } = "";
            public long Size { get; set; }
            public long DownloadSize { get; set; }
        }

        public class ManifestResult
        {
            public string AppId { get; set; } = "";
            public string Name { get; set; } = "";
            public List<ManifestFile> Manifests { get; set; } = new();
            public string? Error { get; set; }
        }

        public class ManifestZipResult
        {
            public byte[] Buffer { get; set; } = Array.Empty<byte>();
            public int FileCount { get; set; }
            public int Skipped { get; set; }
        }

        /// <summary>Load the full fares.top game catalog (cached after first call).</summary>
        public async Task LoadCatalogAsync()
        {
            if (_catalogLoaded) return;
            try
            {
                var json = await _http.GetStringAsync($"{Base}/api/games/catalog");
                var data = JObject.Parse(json);
                var arr = data["d"] as JArray;
                if (arr != null)
                {
                    _catalog = arr.Select(g => new CatalogEntry
                    {
                        Id = (string?)g["i"] ?? "",
                        Name = (string?)g["n"] ?? ""
                    }).Where(e => !string.IsNullOrEmpty(e.Id)).ToList();
                }
            }
            catch { /* catalog optional */ }
            _catalogLoaded = true;
        }

        /// <summary>Search the catalog by name or App ID.</summary>
        public List<CatalogEntry> SearchCatalog(string query, int limit = 25)
        {
            var q = query.ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(q)) return _catalog.Take(limit).ToList();
            if (int.TryParse(q, out _))
            {
                var exact = _catalog.FirstOrDefault(g => g.Id == q);
                if (exact != null) return new List<CatalogEntry> { exact };
            }
            return _catalog
                .Where(g => g.Name.ToLowerInvariant().Contains(q) || g.Id.Contains(q))
                .Take(limit)
                .ToList();
        }

        /// <summary>Get all manifests for an App ID from fares.top.</summary>
        public async Task<ManifestResult> GetManifestsAsync(string appId, int server = 1)
        {
            try
            {
                var json = await _http.GetStringAsync($"{Base}/api/manifest?appId={Uri.EscapeDataString(appId)}&server={server}");
                var data = JObject.Parse(json);
                var err = data["error"]?.Value<string>();
                if (!string.IsNullOrEmpty(err))
                    return new ManifestResult { AppId = appId, Name = appId, Error = err };

                var appData = data["data"]?[appId] as JObject;
                if (appData == null)
                    return new ManifestResult { AppId = appId, Name = appId, Error = "No manifest data" };

                var manifests = new List<ManifestFile>();
                var depots = appData["depots"] as JObject;
                if (depots != null)
                {
                    foreach (var prop in depots.Properties())
                    {
                        var depot = prop.Value as JObject;
                        var pub = depot?["manifests"]?["public"] as JObject;
                        var gid = pub?["gid"]?.Value<string>();
                        if (string.IsNullOrEmpty(gid)) continue;
                        manifests.Add(new ManifestFile
                        {
                            DepotId = depot?["depotid"]?.Value<string>() ?? prop.Name,
                            ManifestId = gid,
                            Size = pub?["size"]?.Value<long>() ?? 0,
                            DownloadSize = pub?["download"]?.Value<long>() ?? 0
                        });
                    }
                }
                manifests.Sort((a, b) => b.Size.CompareTo(a.Size));
                return new ManifestResult
                {
                    AppId = appId,
                    Name = appData["common"]?["name"]?.Value<string>() ?? appId,
                    Manifests = manifests
                };
            }
            catch (Exception ex)
            {
                return new ManifestResult { AppId = appId, Name = appId, Error = ex.Message };
            }
        }

        /// <summary>Download a single manifest's raw bytes.</summary>
        public async Task<byte[]> DownloadManifestAsync(string appId, string depotId, string manifestId, int server = 1)
        {
            var url = $"{Base}/api/manifest/download?depotId={Uri.EscapeDataString(depotId)}&manifestId={Uri.EscapeDataString(manifestId)}&appId={Uri.EscapeDataString(appId)}";
            if (server == 2) url += "&server=2";
            return await _http.GetByteArrayAsync(url);
        }

        /// <summary>Download all manifests for an App ID and bundle into a zip.</summary>
        public async Task<ManifestZipResult?> CreateManifestZipAsync(string appId, List<ManifestFile> manifests, int maxMb = 100)
        {
            var maxSize = maxMb * 1024 * 1024L;
            var files = new List<(string name, byte[] buffer)>();
            int skipped = 0;

            foreach (var m in manifests)
            {
                try
                {
                    var buf = await DownloadManifestAsync(appId, m.DepotId, m.ManifestId);
                    if (buf.Length > 5 * 1024 * 1024) { skipped++; continue; } // skip huge single files
                    files.Add(($"{m.DepotId}_{m.ManifestId}.manifest", buf));
                }
                catch { skipped++; }
            }

            if (files.Count == 0) return null;

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var f in files)
                {
                    var entry = zip.CreateEntry(f.name);
                    using var es = entry.Open();
                    await es.WriteAsync(f.buffer);
                }
            }
            var buffer = ms.ToArray();
            if (buffer.Length > maxSize) return null;
            return new ManifestZipResult { Buffer = buffer, FileCount = files.Count, Skipped = skipped };
        }

        public int CatalogCount => _catalog.Count;
    }
}

