using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Wpf.Ui.Controls;


namespace LeitostrapV7.Core;


public class AutoUpdateService
{
    private static AutoUpdateService? _instance;
    public static AutoUpdateService Instance => _instance ??= new AutoUpdateService();


    private const string GitHubApiUrl = "https://api.github.com/repos/Leitostrap/Leitostrap-Injector/releases/latest";
    private const string CurrentVersion = "V7.0.1";


    public string LatestVersion { get; private set; } = "";
    public string DownloadUrl { get; private set; } = "";
    public string ReleaseNotes { get; private set; } = "";
    public bool UpdateAvailable { get; private set; }


    private AutoUpdateService() { }


    public async Task CheckForUpdatesAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "Leitostrap-Injector");

            string json = await client.GetStringAsync(GitHubApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            LatestVersion = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
            ReleaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";

            if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
            {
                var asset = assets[0];
                DownloadUrl = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : "";
            }

            UpdateAvailable = !string.IsNullOrEmpty(LatestVersion) &&
                             !string.IsNullOrEmpty(DownloadUrl) &&
                             LatestVersion.TrimStart('V', 'v') != CurrentVersion.TrimStart('V', 'v');
        }
        catch
        {
            UpdateAvailable = false;
        }
    }


    public void ShowUpdateCard(StackPanel toastHost)
    {
        if (!UpdateAvailable) return;


        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
        accentBrush.Freeze();

        var bgBrush = new SolidColorBrush(Color.FromArgb(22, 245, 158, 11));
        bgBrush.Freeze();

        var subtleBrush = new SolidColorBrush(Color.FromArgb(100, 245, 158, 11));
        subtleBrush.Freeze();

        var textBrush = new SolidColorBrush(Colors.White);
        textBrush.Freeze();

        var dimBrush = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        dimBrush.Freeze();


        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.ArrowDownload20,
            FontSize = 16,
            Foreground = accentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };


        var titleText = new TextBlock
        {
            Text = $"Update Available: {LatestVersion}",
            FontSize = 12,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };


        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { icon, titleText }
        };


        var notesText = new TextBlock
        {
            Text = string.IsNullOrEmpty(ReleaseNotes) ? "New version ready to install." : ReleaseNotes,
            FontSize = 11,
            Foreground = dimBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 340,
            Margin = new Thickness(0, 6, 0, 0)
        };


        var progressBarBg = new Border
        {
            Height = 4,
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 10, 0, 0),
            ClipToBounds = true
        };

        var progressBarFill = new Border
        {
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2)
        };
        progressBarFill.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(245, 158, 11), 0),
                new GradientStop(Color.FromRgb(217, 119, 6), 1)
            }
        };
        progressBarBg.Child = progressBarFill;


        var progressLabel = new TextBlock
        {
            Text = "",
            FontSize = 10,
            Foreground = dimBrush,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };


        var updateBtn = new Button
        {
            Content = "Update Now",
            FontSize = 11,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Black),
            Background = accentBrush,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(16, 5, 16, 5),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dismissBtn = new Button
        {
            Content = "Dismiss",
            FontSize = 11,
            Foreground = dimBrush,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };


        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { updateBtn, dismissBtn }
        };


        var cardContent = new StackPanel
        {
            Children = { headerRow, notesText, progressBarBg, progressLabel, btnRow }
        };


        var card = new Border
        {
            Background = bgBrush,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = cardContent
        };


        card.RenderTransform = new TranslateTransform(350, 0);
        Application.Current.Dispatcher.Invoke(() =>
        {
            toastHost.Children.Add(card);

            var slideIn = new DoubleAnimation(350, 0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            card.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            card.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        });


        bool dismissed = false;

        dismissBtn.Click += (_, _) =>
        {
            dismissed = true;
            RemoveCard(card, toastHost);
        };

        updateBtn.Click += async (_, _) =>
        {
            updateBtn.IsEnabled = false;
            updateBtn.Content = "Downloading...";
            dismissBtn.IsEnabled = false;

            try
            {
                await DownloadAndInstallAsync((downloaded, total) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (total > 0)
                        {
                            double pct = (double)downloaded / total * 100;
                            double trackWidth = 340;
                            progressBarFill.Width = trackWidth * pct / 100.0;
                            progressLabel.Text = $"{pct:F0}% - {downloaded / 1024.0 / 1024.0:F1} MB / {total / 1024.0 / 1024.0:F1} MB";
                            progressLabel.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            progressLabel.Text = $"{downloaded / 1024.0 / 1024.0:F1} MB downloaded...";
                            progressLabel.Visibility = Visibility.Visible;
                        }
                    });
                });
            }
            catch
            {
                updateBtn.Content = "Retry";
                updateBtn.IsEnabled = true;
                dismissBtn.IsEnabled = true;
            }
        };
    }


    private void RemoveCard(Border card, StackPanel host)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var slideOut = new DoubleAnimation(0, 350, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) => host.Children.Remove(card);
            card.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
            card.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        });
    }


    public async Task DownloadAndInstallAsync(Action<double, double>? onProgress = null)
    {
        if (string.IsNullOrEmpty(DownloadUrl)) return;

        string tempPath = Path.Combine(Path.GetTempPath(), "Leitostrap-Injector-Update.exe");

        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("User-Agent", "Leitostrap-Injector");

        using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(tempPath);

        byte[] buffer = new byte[8192];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            downloaded += bytesRead;
            onProgress?.Invoke(downloaded, totalBytes ?? 0);
        }

        fileStream.Close();

        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }
}
