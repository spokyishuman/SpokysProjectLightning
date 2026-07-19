using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SpokysProjectLightning.Services
{
    public class DownloadService : INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private static string DownloadPath => DataService.GetDownloadPath();

        private double _progress;
        private string _status = "Ready";
        private string _currentFile = "";
        private long _bytesReceived;
        private long _totalBytes;
        private bool _isDownloading;

        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(nameof(Progress)); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }
        public string CurrentFile { get => _currentFile; set { _currentFile = value; OnPropertyChanged(nameof(CurrentFile)); } }
        public long BytesReceived { get => _bytesReceived; set { _bytesReceived = value; OnPropertyChanged(nameof(BytesReceived)); } }
        public long TotalBytes { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(nameof(TotalBytes)); } }
        public bool IsDownloading { get => _isDownloading; set { _isDownloading = value; OnPropertyChanged(nameof(IsDownloading)); } }
        public string Speed { get => _speed; private set { _speed = value; OnPropertyChanged(nameof(Speed)); } }
        private string _speed = "0 KB/s";

        public event PropertyChangedEventHandler? PropertyChanged;

        public DownloadService()
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
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            Directory.CreateDirectory(DownloadPath);
        }

        public async Task<string> DownloadFileAsync(string url, string fileName, CancellationToken cancellationToken = default)
        {
            IsDownloading = true;
            Status = "Starting download...";
            Progress = 0;

            try
            {
                string filePath = Path.Combine(DownloadPath, SanitizeFileName(fileName));
                CurrentFile = fileName;

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                TotalBytes = response.Content.Headers.ContentLength ?? -1;
                var totalRead = 0L;
                var buffer = new byte[65536];
                var stopwatch = Stopwatch.StartNew();
                var lastBytesRead = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                while (true)
                {
                    var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;
                    BytesReceived = totalRead;

                    if (TotalBytes > 0)
                    {
                        Progress = (double)totalRead / TotalBytes * 100;
                        Status = $"Downloading... {Progress:F1}%";
                    }
                    else
                    {
                        Status = $"Downloading... {FormatSize(totalRead)}";
                    }

                    // Calculate speed
                    if (stopwatch.ElapsedMilliseconds >= 1000)
                    {
                        var speed = (totalRead - lastBytesRead) / (stopwatch.ElapsedMilliseconds / 1000.0);
                        Speed = $"{FormatSize((long)speed)}/s";
                        lastBytesRead = totalRead;
                        stopwatch.Restart();
                    }
                }

                Status = "Download complete!";
                Progress = 100;
                return filePath;
            }
            catch (OperationCanceledException)
            {
                Status = "Download cancelled";
                return string.Empty;
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                return string.Empty;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        public void OpenDownloadsFolder()
        {
            try
            {
                Process.Start("explorer.exe", DownloadPath);
            }
            catch { }
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
                fileName = fileName.Replace(c, '_');
            return fileName;
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:F1} {suffixes[i]}";
        }

        protected void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

