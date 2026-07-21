using System;
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
                var appVersion = "1.3.3.0";
                try
                {
                    var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (ver != null) appVersion = ver.ToString();
                }
                catch { }

                var detail = $"**Bug Report**\n{description}\n\n---\nApp: Spoky's Project Lightning v{appVersion}\nOS: {Environment.OSVersion}";

                // Direct Discord webhook → multipart with optional video
                if (webhookUrl.Contains("discord.com/api/webhooks"))
                {
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(detail, Encoding.UTF8), "content");

                    if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                    {
                        var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
                        form.Add(new StreamContent(fileStream), "file", Path.GetFileName(videoPath));
                    }

                    var resp = await _http.PostAsync(webhookUrl, form);
                    var body = await resp.Content.ReadAsStringAsync();
                    return resp.IsSuccessStatusCode
                        ? (true, "Report sent! Thank you.")
                        : (false, $"Server returned {(int)resp.StatusCode}: {body}");
                }

                // Generic endpoint (Vercel proxy) → JSON, text-only
                var json = System.Text.Json.JsonSerializer.Serialize(new { content = detail });
                var resp2 = await _http.PostAsync(webhookUrl, new StringContent(json, Encoding.UTF8, "application/json"));
                var body2 = await resp2.Content.ReadAsStringAsync();
                return resp2.IsSuccessStatusCode
                    ? (true, "Report sent! Thank you.")
                    : (false, $"Server returned {(int)resp2.StatusCode}: {body2}");
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
