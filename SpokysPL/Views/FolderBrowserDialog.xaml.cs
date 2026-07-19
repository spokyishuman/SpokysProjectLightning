using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SpokysProjectLightning.Views
{
    /// <summary>Lightweight, dependency-free folder picker built on a WPF TreeView.</summary>
    public partial class FolderBrowserDialog : Window
    {
        public string? SelectedPath { get; private set; }

        public FolderBrowserDialog(string description = "Select a folder")
        {
            InitializeComponent();
            Title = description;
            Loaded += (_, _) => PopulateDrives();
        }

        private void PopulateDrives()
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var item = new TreeViewItem
                    {
                        Header = drive.RootDirectory.FullName,
                        Tag = drive.RootDirectory.FullName,
                        Items = { Placeholder() }
                    };
                    item.Expanded += Folder_Expanded;
                    Tree.Items.Add(item);
                }
            }
            catch { }
        }

        private static TreeViewItem Placeholder() => new() { Header = "…" };

        private void Folder_Expanded(object? sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem item || item.Tag is not string path) return;
            if (item.Items.Count == 1 && item.Items[0] is TreeViewItem ph && ph.Header as string == "…")
            {
                item.Items.Clear();
                try
                {
                    foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                    {
                        var sub = new TreeViewItem
                        {
                            Header = Path.GetFileName(dir.TrimEnd('\\', '/')),
                            Tag = dir,
                            Items = { Placeholder() }
                        };
                        sub.Expanded += Folder_Expanded;
                        item.Items.Add(sub);
                    }
                }
                catch { /* access denied — leave empty */ }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Tree.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                SelectedPath = path;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

