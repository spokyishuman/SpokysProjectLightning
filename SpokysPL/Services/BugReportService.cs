using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SpokysProjectLightning.Services
{
    public class BugReportService
    {
        private readonly HttpClient _http;

        public BugReportService()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler);
            _http.DefaultRequestHeaders.Add("User-Agent", "SpokysPL-BugReport/1.0");
            _http.Timeout = TimeSpan.FromMinutes(5);
        }

        public async Task<(bool Success, string Message)> SendReportAsync(string webhookUrl, string description, string? videoPath = null)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return (false, "No webhook URL configured. Set it in Settings.");

            if (string.IsNullOrWhiteSpace(description))
                return (false, "Please describe the bug.");

            try
            {
                using var form = new MultipartFormDataContent();

                var appVersion = "1.3.3.0";
                try
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (ver != null) appVersion = ver.ToString();
                }
                catch { }

                var detail = $"**Bug Report**\n{description}\n\n---\nApp: Spoky's Project Lightning v{appVersion}\nOS: {Environment.OSVersion}";

                form.Add(new StringContent(detail, Encoding.UTF8), "content");

                if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                {
                    var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
                    var fileName = Path.GetFileName(videoPath);
                    form.Add(new StreamContent(fileStream), "file", fileName);
                }

                var response = await _http.PostAsync(webhookUrl, form);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                    return (true, "Report sent! Thank you.");

                return (false, $"Server returned {(int)response.StatusCode}: {body}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Request timed out. File may be too large.");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }
    }
}
