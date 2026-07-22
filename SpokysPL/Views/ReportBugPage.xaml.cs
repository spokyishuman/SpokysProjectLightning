using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SpokysProjectVercel.Services;

namespace SpokysProjectVercel.Views
{
    public partial class ReportBugPage : UserControl
    {
        private readonly BugReportService _bugReport = new();
        private string? _selectedVideoPath;

        public ReportBugPage()
        {
            InitializeComponent();
        }

        private void SelectVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video files (*.mp4;*.webm;*.mkv;*.avi;*.mov;*.wmv)|*.mp4;*.webm;*.mkv;*.avi;*.mov;*.wmv|All files (*.*)|*.*",
                Title = "Select a video showing the bug"
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedVideoPath = dialog.FileName;
                VideoPathDisplay.Text = _selectedVideoPath;
                VideoPathDisplay.Foreground = TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush;
                ClearVideoBtn.Visibility = Visibility.Visible;
                VideoIcon.Text = "🎬";

                try
                {
                    var info = new FileInfo(_selectedVideoPath);
                    if (info.Exists)
                    {
                        var sizeMB = info.Length / (1024.0 * 1024.0);
                        VideoSizeText.Text = $"{info.Length:N0} bytes ({sizeMB:F1} MB)";
                        if (sizeMB > 25)
                            VideoSizeText.Text += " — may be too large for upload";
                    }
                }
                catch { }
            }
        }

        private void ClearVideo_Click(object sender, RoutedEventArgs e) => ClearVideo();

        private void ClearVideo()
        {
            _selectedVideoPath = null;
            VideoPathDisplay.Text = "No video selected";
            VideoPathDisplay.Foreground = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
            ClearVideoBtn.Visibility = Visibility.Collapsed;
            VideoIcon.Text = "🎥";
            VideoSizeText.Text = "";
        }

        private async void SendReport_Click(object sender, RoutedEventArgs e)
        {
            var description = DescriptionBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                StatusText.Text = "Please describe the bug.";
                return;
            }

            var settings = new DataService().LoadSettings();
            var webhookUrl = settings.BugReportWebhookUrl;

            if (string.IsNullOrWhiteSpace(webhookUrl))
            {
                StatusText.Text = "No webhook URL set. Go to Settings.";
                return;
            }

            SendBtn.IsEnabled = false;
            SendBtn.Content = "⏳ Sending...";
            StatusText.Text = "";

            var (success, message) = await _bugReport.SendReportAsync(webhookUrl, description, _selectedVideoPath);

            SendBtn.IsEnabled = true;
            SendBtn.Content = "🐛 Send Report";
            StatusText.Text = message;

            if (success)
            {
                DescriptionBox.Clear();
                ClearVideo();
            }
        }
    }
}
