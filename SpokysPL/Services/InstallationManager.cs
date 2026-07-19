using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SpokysProjectLightning.Services
{
    public class InstallTask : INotifyPropertyChanged
    {
        public string AppId { get; set; } = "";
        public string GameName { get; set; } = "";

        private string _status = "Queued";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

        private double _progressPercent;
        public double ProgressPercent { get => _progressPercent; set { _progressPercent = value; OnPropertyChanged(); } }

        private bool _isComplete;
        public bool IsComplete { get => _isComplete; set { _isComplete = value; OnPropertyChanged(); } }

        private bool _isError;
        public bool IsError { get => _isError; set { _isError = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class InstallationManager : INotifyPropertyChanged
    {
        private static readonly Lazy<InstallationManager> _instance = new(() => new());
        public static InstallationManager Instance => _instance.Value;

        private readonly ManifestService _manifest = new();
        private readonly RyuuFixesService _fixes = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private int _activeCount;

        public ObservableCollection<InstallTask> Tasks { get; } = new();
        public bool HasActiveTasks => Tasks.Any(t => !t.IsComplete);

        public int ActiveCount
        {
            get => _activeCount;
            set { _activeCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasActiveTasks)); }
        }

        public double OverallProgress
        {
            get
            {
                var total = Tasks.Count;
                if (total == 0) return 0;
                return (double)Tasks.Count(t => t.IsComplete) / total * 100;
            }
        }

        public string QueueStatus
        {
            get
            {
                var remaining = Tasks.Count(t => !t.IsComplete);
                return remaining == 0 ? "Ready" : $"{remaining} remaining";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private InstallationManager() { }

        public async Task<ManifestService.InstallResult> EnqueueAsync(string appId, string gameName)
        {
            var task = new InstallTask { AppId = appId, GameName = gameName };

            await Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Tasks.Add(task);
                NotifyProps();
            });

            await _semaphore.WaitAsync();
            ActiveCount++;
            try
            {
                task.Status = "Installing manifest...";

                var progress = new Progress<(int done, int total, string file)>(p =>
                {
                    task.ProgressPercent = p.total > 0 ? (double)p.done / p.total * 100 : 0;
                    task.Status = $"[{p.done}/{p.total}] {p.file}";
                });

                var result = await _manifest.InstallGameManifestAsync(appId, gameName, null, progress);

                if (result.Success)
                {
                    task.Status = "Checking for fix...";

                    var fix = await _fixes.GetFixForAppAsync(appId);
                    if (fix != null)
                    {
                        task.Status = "Downloading fix...";

                        var fixBytes = await _fixes.DownloadFixAsync(fix.Href);
                        if (fixBytes != null)
                        {
                            var fixDir = System.IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpokyFixes");
                            System.IO.Directory.CreateDirectory(fixDir);
                            var fixPath = System.IO.Path.Combine(fixDir, fix.FileName);
                            await System.IO.File.WriteAllBytesAsync(fixPath, fixBytes);
                            task.Status = $"Fix saved: {fix.FileName}";
                        }
                    }

                    task.Status = "Complete";
                }
                else
                {
                    task.Status = $"Failed: {result.Message}";
                    task.IsError = true;
                }

                task.IsComplete = true;
                NotifyProps();
                return result;
            }
            catch (Exception ex)
            {
                task.Status = $"Error: {ex.Message}";
                task.IsError = true;
                task.IsComplete = true;
                NotifyProps();
                return new ManifestService.InstallResult { Success = false, Message = ex.Message };
            }
            finally
            {
                ActiveCount--;
                _semaphore.Release();
                await Application.Current.Dispatcher.BeginInvoke(() => NotifyProps());
            }
        }

        public void ClearCompleted()
        {
            var completed = Tasks.Where(t => t.IsComplete).ToList();
            foreach (var t in completed)
                Tasks.Remove(t);
            NotifyProps();
        }

        private void NotifyProps()
        {
            OnPropertyChanged(nameof(OverallProgress));
            OnPropertyChanged(nameof(QueueStatus));
            OnPropertyChanged(nameof(HasActiveTasks));
            OnPropertyChanged(nameof(ActiveCount));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
