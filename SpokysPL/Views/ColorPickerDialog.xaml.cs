using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpokysProjectLightning.Views
{
    public partial class ColorPickerDialog : Window
    {
        public string SelectedHex { get; private set; } = "#FFFFFF";

        private bool _isUpdating = true;

        public ColorPickerDialog(string currentHex)
        {
            InitializeComponent();
            SelectedHex = currentHex;
            try { ApplyHex(currentHex); } catch { }
            _isUpdating = false;
        }

        private void ApplyHex(string hex)
        {
            _isUpdating = true;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            RSlider.Value = color.R;
            GSlider.Value = color.G;
            BSlider.Value = color.B;
            RBox.Text = color.R.ToString();
            GBox.Text = color.G.ToString();
            BBox.Text = color.B.ToString();
            HexBox.Text = hex;
            UpdatePreview(color);
            _isUpdating = false;
        }

        private void UpdatePreview(Color color)
        {
            PreviewSwatch.Background = new SolidColorBrush(color);
            HexDisplay.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            RgbDisplay.Text = $"RGB({color.R}, {color.G}, {color.B})";
            SelectedHex = HexDisplay.Text;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating) return;
            _isUpdating = true;
            var r = (byte)RSlider.Value;
            var g = (byte)GSlider.Value;
            var b = (byte)BSlider.Value;
            RBox.Text = r.ToString();
            GBox.Text = g.ToString();
            BBox.Text = b.ToString();
            var hex = $"#{r:X2}{g:X2}{b:X2}";
            HexBox.Text = hex;
            UpdatePreview(Color.FromRgb(r, g, b));
            _isUpdating = false;
        }

        private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (!(sender is TextBox tb)) return;
            if (byte.TryParse(tb.Text, out var val))
            {
                _isUpdating = true;
                if (tb == RBox) RSlider.Value = val;
                else if (tb == GBox) GSlider.Value = val;
                else if (tb == BBox) BSlider.Value = val;
                var r = (byte)RSlider.Value;
                var g = (byte)GSlider.Value;
                var b = (byte)BSlider.Value;
                HexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
                UpdatePreview(Color.FromRgb(r, g, b));
                _isUpdating = false;
            }
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(HexBox.Text);
                ApplyHex(HexBox.Text);
            }
            catch { }
        }

        private void Presets_Click(object sender, RoutedEventArgs e)
        {
            if (PresetsPopup.IsOpen) { PresetsPopup.IsOpen = false; return; }

            PresetsPanel.Children.Clear();
            string[] presetHexes = {
                "#E94560", "#FF6B7B", "#F44336", "#FF9800", "#FFC107",
                "#4CAF50", "#2196F3", "#00BCD4", "#9C27B0", "#E040FB",
                "#F0F0F0", "#9090A8", "#606078", "#1A1A2E", "#0F0F1E",
                "#FFFFFF", "#000000", "#2A2A4A", "#4ADE80", "#3F51B5"
            };
            foreach (var h in presetHexes)
            {
                var swatch = new Border
                {
                    Width = 22, Height = 22, Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4), Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                    Tag = h
                };
                try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); } catch { }
                swatch.MouseLeftButtonUp += (s, ev) =>
                {
                    if (s is Border b && b.Tag is string hex)
                    {
                        ApplyHex(hex);
                        PresetsPopup.IsOpen = false;
                    }
                };
                PresetsPanel.Children.Add(swatch);
            }
            PresetsPopup.IsOpen = true;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

