using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpokysProjectVercel.Services
{
    public static class ToastService
    {
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

        public static void Show(string message, string type = "info", int durationMs = 4000)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var window = Application.Current.MainWindow;
                if (window == null) return;

                var container = window.FindName("ToastContainer") as Panel;
                if (container == null) return;

                var accent = type switch
                {
                    "success" => "#4ADE80",
                    "error" => "#F44336",
                    "warning" => "#FF9800",
                    _ => "#4ADE80"
                };
                var icon = type switch
                {
                    "success" => "✅",
                    "error" => "❌",
                    "warning" => "⚠️",
                    _ => "ℹ️"
                };

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1A, 0x1A, 0x30)),
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Opacity = 0,
                    RenderTransform = new TranslateTransform(400, 0),
                    MaxWidth = 380
                };
                border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString(accent),
                    Opacity = 0.3
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var iconBlock = new TextBlock
                {
                    Text = icon,
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 10, 0)
                };
                Grid.SetColumn(iconBlock, 0);

                var textBlock = new TextBlock
                {
                    Text = message,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(textBlock, 1);

                grid.Children.Add(iconBlock);
                grid.Children.Add(textBlock);
                border.Child = grid;

                container.Children.Add(border);

                var slideIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var translateIn = new DoubleAnimation(400, 0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                border.BeginAnimation(UIElement.OpacityProperty, slideIn);
                border.RenderTransform.BeginAnimation(TranslateTransform.XProperty, translateIn);

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(durationMs)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (_, _) =>
                    {
                        container.Children.Remove(border);
                    };
                    border.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                timer.Start();
            });
        }
    }
}

