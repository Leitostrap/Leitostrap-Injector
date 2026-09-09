using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LeitostrapV7.Core;
using Microsoft.Win32;


namespace LeitostrapV7.Views;


public partial class HomeView : UserControl
{
    private readonly DispatcherTimer _robloxTimer;
    private bool _smartMode;
    private int _fflagsInjectedCount;
    private string _robloxExePath = string.Empty;
    private bool _robloxDetected;
    private bool _applyOverlayVisible;
    private bool _autoInjectRunning;


    public HomeView()
    {
        InitializeComponent();
        _robloxTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _robloxTimer.Tick += (s, e) => CheckRobloxStatus();
        _robloxTimer.Start();
        Loaded += OnLoaded;
    }


    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        LoadFFlagsCount();
        await UpdateVersionFromApi();
        CheckRobloxStatus();
        if (!OffsetService.Instance.IsFetched)
        {
            try { await Task.WhenAny(OffsetService.Instance.FetchOffsetsAsync(), Task.Delay(15000)); }
            catch { }
        }
        try { await Task.WhenAny(OffsetService.Instance.WaitForCacheOffsets(), Task.Delay(10000)); }
        catch { }
        UpdateStatus();
    }


    private async Task AutoInjectAsync()
    {
        if (_autoInjectRunning || Combined.Instance.IsInjecting) return;
        _autoInjectRunning = true;
        try
        {
            var flags = FFlagService.Instance.CurrentFlags;
            if (flags.Count == 0)
            {
                ConsoleService.Instance.Log("No FFlags loaded — skipping auto-inject", "Info");
                return;
            }


            var flatDict = new Dictionary<string, string>();
            foreach (var f in flags)
                if (!string.IsNullOrWhiteSpace(f.Value))
                    flatDict[f.Name] = f.Value;


            if (flatDict.Count == 0)
            {
                ConsoleService.Instance.Log("No FFlags with values — skipping auto-inject", "Info");
                return;
            }


            string methodStr = SettingsService.Instance.Get<string>("InjectionMethod", "memory");
            var method = methodStr switch
            {
                "cache" => Combined.InjectionMethod.CacheMethod,
                "memory" => Combined.InjectionMethod.MemoryOffsets,
                "offsetless" => Combined.InjectionMethod.MemoryOffsetless,
                _ => Combined.InjectionMethod.Combined
            };


            if (method == Combined.InjectionMethod.CacheMethod)
            {
                ConsoleService.Instance.Log("Auto-inject skipped: Cache Method is not supported for auto-inject", "Info");
                return;
            }


            bool needsAdmin = method != Combined.InjectionMethod.CacheMethod;
            if (needsAdmin && !CacheMethod.IsAdmin())
            {
                ConsoleService.Instance.Log("Auto-inject: Admin required — relaunching as admin...", "Warning");
                NotificationService.Instance.Show("Relaunching as Administrator...", NotificationType.Warning, 3000);
                await Task.Delay(1000);
                CacheMethod.ElevateAndRelaunch();
                return;
            }


            string methodLabel = method switch
            {
                Combined.InjectionMethod.CacheMethod => "Cache Method",
                Combined.InjectionMethod.MemoryOffsets => "Memory Offsets",
                Combined.InjectionMethod.MemoryOffsetless => "Memory Offsetless",
                _ => "Combined"
            };


            ConsoleService.Instance.Log($"Auto-injecting {flatDict.Count} flags via {methodLabel}...", "Info");
            ShowApplyOverlay(methodLabel);
            SetApplyPhase("waiting");
            SetApplyState("Auto-injecting...", $"Detecting Roblox...", 0);


            Combined.Instance.OnStatusUpdate = (statusMsg, phase) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetApplyPhase(phase);
                    SetApplyState(phase switch
                    {
                        "waiting" => "Waiting for Roblox...",
                        "detecting" => "Detecting right moment...",
                        "injecting" => "Injecting flags...",
                        "done" => "Done!",
                        "error" => "Failed",
                        _ => "Preparing..."
                    }, statusMsg, phase == "done" || phase == "error" ? 1.0 : 0.3);
                }));
            };


            var result = await Combined.Instance.InjectAsync(flatDict, method, (done, total) =>
            {
                if (total > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        double fraction = (double)done / total;
                        SetApplyState($"Injecting flags... ({done}/{total})", $"{done} of {total} flags written", 0.3 + (fraction * 0.7));
                    }));
                }
            });


            _fflagsInjectedCount = result.ok ? flatDict.Count : 0;
            UpdateStatus();


            if (result.ok)
            {
                SetApplyPhase("done");
                SetApplyState("Injected!", result.msg, 1.0);
                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                ApplyResultText.Text = $"{flatDict.Count} FastFlags injected automatically";
                ConsoleService.Instance.Log($"Auto-injected {flatDict.Count} FastFlags", "Success");
            }
            else
            {
                SetApplyPhase("error");
                SetApplyState("Failed", result.msg, 1.0);
                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ApplyResultText.Text = result.msg;
                ConsoleService.Instance.Log($"Auto-inject failed: {result.msg}", "Error");
            }


            await Task.Delay(2200);
            HideApplyOverlay();
            Combined.Instance.OnStatusUpdate = null;
        }
        catch (Exception ex)
        {
            HideApplyOverlay();
            Combined.Instance.OnStatusUpdate = null;
            ConsoleService.Instance.Log($"Auto-inject error: {ex.Message}", "Error");
        }
        finally
        {
            _autoInjectRunning = false;
        }
    }


    private void LoadSettings()
    {
        try
        {
        _smartMode = SettingsService.Instance.Get<bool>("SmartMode", true);
        _robloxExePath = SettingsService.Instance.Get<string>("RobloxExePath") ?? string.Empty;
        UpdateSmartModeVisual(false);
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"Failed to load settings: {ex.Message}", "Error"); }
    }


    private void LoadFFlagsCount()
    {
        try
        {
            string fflagsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Leitostrap Injector", "FFlags", "current_fflags.json");
            if (File.Exists(fflagsPath))
                FFlagService.Instance.ImportFromJson(fflagsPath);
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"Failed to load FFlags: {ex.Message}", "Error"); }
    }


    private async Task UpdateVersionFromApi()
    {
        try
        {
            using var handler = new System.Net.Http.HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Leitostrap/7.0");
            string json = await client.GetStringAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("clientVersionUpload", out var ver))
            {
                string version = ver.GetString() ?? "";
                if (!string.IsNullOrEmpty(version))
                {
                    RobloxVersionText.Text = version.StartsWith("version-") ? version : "version-" + version;
                    if (OffsetService.Instance.CurrentVersion != version)
                        OffsetService.Instance.SetCurrentVersion(version);
                }
            }
        }
        catch { }
    }


    private void CheckRobloxStatus()
    {
        try
        {
            bool running = ProcessService.Instance.IsRunning();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (running)
                {
                    if (!_robloxDetected)
                    {
                        _robloxDetected = true;
                        if (_smartMode)
                        {
                            ConsoleService.Instance.Log("Roblox detected — waiting 3s to load, then injecting...", "Success");


                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(3000);
                                await Dispatcher.InvokeAsync(async () => await AutoInjectAsync());
                            });
                        }
                        else
                        {
                            ConsoleService.Instance.Log("Roblox detected — Smart Mode is off, skipping auto-inject", "Info");
                        }
                    }
                    string version = ProcessService.Instance.GetRobloxVersion();
                    if (!string.IsNullOrEmpty(version) && version != "Unknown")
                        RobloxVersionText.Text = version.StartsWith("version-") ? version : "version-" + version;
                    RobloxStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    RobloxStatusText.Text = "Running";
                    RobloxStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                }
                else
                {
                    if (_robloxDetected) { _robloxDetected = false; _fflagsInjectedCount = 0; UpdateStatus(); ConsoleService.Instance.Log("Roblox closed — flags reset", "Warning"); }
                    RobloxStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
                    RobloxStatusText.Text = "Not Running";
                    RobloxStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                }
            }));
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"Roblox check error: {ex.Message}", "Error"); }
    }


    private void UpdateStatus()
    {
        CacheOffsetsCount.Text = OffsetService.Instance.GetAllCacheOffsets().Count.ToString("N0");
        MemoryOffsetsCount.Text = OffsetService.Instance.GetAllMemoryOffsets().Count.ToString("N0");
        FFlagsLoadedCount.Text = FFlagService.Instance.CurrentFlags.Count.ToString();
        int total = FFlagService.Instance.CurrentFlags.Count;
        FFlagsInjectedCount.Text = _fflagsInjectedCount.ToString();
        FFlagsInjectedTotal.Text = total.ToString();


        if (_fflagsInjectedCount > 0 && _fflagsInjectedCount >= total)
        {


            FFlagsInjectedCount.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            FFlagsInjectedSeparator.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            FFlagsInjectedTotal.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            InjectedDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            InjectedStatus.Text = "All flags injected";
            InjectedStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        }
        else if (_fflagsInjectedCount > 0)
        {


            FFlagsInjectedCount.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            FFlagsInjectedSeparator.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            FFlagsInjectedTotal.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            InjectedDot.Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            InjectedStatus.Text = $"{_fflagsInjectedCount} of {total} injected";
            InjectedStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
        else
        {


            FFlagsInjectedCount.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            FFlagsInjectedSeparator.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
            FFlagsInjectedTotal.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            InjectedDot.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            InjectedStatus.Text = "No flags injected";
            InjectedStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        }
    }


    private bool _toggleInitialized;


    private void UpdateSmartModeVisual(bool animate = true)
    {
        if (SmartModeKnob == null) return;
        var track = SmartModeKnob.Parent as Border;


        double targetMarginLeft = _smartMode ? 18 : 2;
        var bgColor = _smartMode ? Color.FromRgb(0x33, 0x33, 0x33) : Color.FromRgb(0x2A, 0x2A, 0x2A);

        if (track != null)
        {
            var freshBrush = new SolidColorBrush(bgColor);
            if (animate)
            {
                var anim = new ColorAnimation(bgColor, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                freshBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            track.Background = freshBrush;
        }
        var slide = new ThicknessAnimation(new Thickness(targetMarginLeft, 0, 0, 0), TimeSpan.FromMilliseconds(animate ? 200 : 0))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        SmartModeKnob.BeginAnimation(MarginProperty, slide);
    }


    private async void ReloadCacheOffsets_Click(object sender, RoutedEventArgs e)
    {
        await OffsetService.Instance.FetchOffsetsAsync();
        UpdateStatus();
        ConsoleService.Instance.Log("Cache offsets reloaded", "Info");
        NotificationService.Instance.Show("Cache offsets reloaded", NotificationType.Info, 2000);
        SpinSyncIcon(sender);
    }


    private async void ReloadMemoryOffsets_Click(object sender, RoutedEventArgs e)
    {
        await OffsetService.Instance.FetchOffsetsAsync();
        UpdateStatus();
        ConsoleService.Instance.Log("Memory offsets reloaded", "Info");
        NotificationService.Instance.Show("Memory offsets reloaded", NotificationType.Info, 2000);
        SpinSyncIcon(sender);
    }


    private void SpinSyncIcon(object sender)
    {
        if (sender is Button btn && btn.Content is StackPanel sp)
        {
            foreach (var child in sp.Children)
            {
                if (child is Wpf.Ui.Controls.SymbolIcon sym && sym.Symbol == Wpf.Ui.Controls.SymbolRegular.ArrowSync20)
                {
                    var rotate = new RotateTransform(0);
                    sym.RenderTransform = rotate;
                    sym.RenderTransformOrigin = new Point(0.5, 0.5);
                    var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(500))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
                    break;
                }
            }
        }
    }


    private void BrowseRobloxExe_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo(6);
    }


    private void ImportFFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "Import FastFlags", Filter = "JSON Files|*.json|All Files|*.*" };
            if (dialog.ShowDialog() == true)
            {
                int before = FFlagService.Instance.CurrentFlags.Count;
                FFlagService.Instance.ImportFromJson(dialog.FileName);
                int imported = FFlagService.Instance.CurrentFlags.Count - before;
                string fflagsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leitostrap Injector", "FFlags");
                if (!Directory.Exists(fflagsDir)) Directory.CreateDirectory(fflagsDir);
                File.Copy(dialog.FileName, Path.Combine(fflagsDir, "current_fflags.json"), true);
                UpdateStatus();
                DiscordService.Instance.UpdateFlagCount(FFlagService.Instance.CurrentFlags.Count);
                ConsoleService.Instance.Log($"Imported {imported} FastFlags", "Success");
                NotificationService.Instance.Show($"Imported {imported} FastFlags", NotificationType.Success);
            }
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"Import error: {ex.Message}", "Error"); }
    }


    private void ReadDocs_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.NavigateTo(10);
    }


    private void ViewFFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewFFlagsList.Children.Clear();
            var flags = FFlagService.Instance.CurrentFlags;
            int count = 0;
            foreach (var f in flags)
            {
                if (string.IsNullOrWhiteSpace(f.Value)) continue;
                count++;
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 3)
                };
                var grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });


                var nameText = new TextBlock
                {
                    Text = f.Name,
                    FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                System.Windows.Controls.Grid.SetColumn(nameText, 0);
                grid.Children.Add(nameText);


                var valueText = new TextBlock
                {
                    Text = f.Value,
                    FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetColumn(valueText, 2);
                grid.Children.Add(valueText);


                row.Child = grid;
                ViewFFlagsList.Children.Add(row);
            }
            ViewFFlagsCount.Text = $"{count} flags";
            ViewFFlagsOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"View error: {ex.Message}", "Error"); }
    }


    private void CloseViewFFlags_Click(object sender, RoutedEventArgs e)
    {
        ViewFFlagsOverlay.Visibility = Visibility.Collapsed;
    }


    private void JoinDiscord_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/leitostrap", UseShellExecute = true }); }
        catch (Exception ex) { ConsoleService.Instance.Log($"Failed to open Discord: {ex.Message}", "Error"); }
    }


    private void SmartModeToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _smartMode = !_smartMode;
        SettingsService.Instance.Set("SmartMode", _smartMode);
        UpdateSmartModeVisual(true);
        ConsoleService.Instance.Log($"Smart mode: {(_smartMode ? "ON" : "OFF")}", "Info");
        NotificationService.Instance.Show($"Smart mode {(_smartMode ? "ON" : "OFF")}", NotificationType.Info, 2000);
    }


    private void ShowApplyOverlay(string method)
    {
        if (_applyOverlayVisible) return;
        _applyOverlayVisible = true;


        ApplyResultBorder.Visibility = Visibility.Collapsed;
        ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        ApplyStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));
        ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        ApplyStatusRing.Fill = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));


        SetApplyState("Preparing...", "", 0);
        ApplyMethodText.Text = method;
        ApplyProgressFill.Width = 0;


        ApplyOverlay.Visibility = Visibility.Visible;
        ApplyCardScale.ScaleX = 0.92;
        ApplyCardScale.ScaleY = 0.92;
        ApplyCardSlide.Y = 20;


        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scaleIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease };
        var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease };
        ApplyCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        ApplyCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        ApplyCardSlide.BeginAnimation(TranslateTransform.YProperty, slideIn);


        StartApplyPulse();
    }


    private void HideApplyOverlay()
    {


        _applyOverlayVisible = false;
        StopApplyPulse();
        ApplyOverlay.Visibility = Visibility.Collapsed;
        ApplyOverlay.BeginAnimation(OpacityProperty, null);
        ApplyOverlay.Opacity = 1;
    }


    private DispatcherTimer? _applyPulseTimer;


    private void StartApplyPulse()
    {
        StopApplyPulse();
        bool toggled = false;
        _applyPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _applyPulseTimer.Tick += (_, _) =>
        {
            toggled = !toggled;
            var ring = toggled
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            var fill = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));


            var animRing = new ColorAnimation(ring.Color, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            ApplyStatusRing.Stroke.BeginAnimation(SolidColorBrush.ColorProperty, animRing);


            var animFill = new ColorAnimation(fill.Color, TimeSpan.FromMilliseconds(600))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            ApplyStatusRing.Fill.BeginAnimation(SolidColorBrush.ColorProperty, animFill);
        };
        _applyPulseTimer.Start();
    }


    private void StopApplyPulse()
    {
        _applyPulseTimer?.Stop();
        _applyPulseTimer = null;
    }


    private void SetApplyState(string title, string status, double progressFraction)
    {
        ApplyTitleText.Text = title;
        ApplyStatusText.Text = status;
        if (progressFraction >= 0 && progressFraction <= 1)
        {
            const double trackWidth = 340;
            double target = Math.Round(progressFraction * trackWidth);
            var w = new DoubleAnimation(target, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ApplyProgressFill.BeginAnimation(WidthProperty, w);
        }
    }


    private void SetApplyPhase(string phase)
    {
        var whiteBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        var darkBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));
        var dimBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0E));


        switch (phase)
        {
            case "waiting":
                ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
                ApplyStatusDot.Fill = darkBrush;
                ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Clock20;
                ApplyStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                break;


            case "detecting":
                ApplyStatusRing.Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                ApplyStatusDot.Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                ApplyStatusDot.Fill = darkBrush;
                ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Search20;
                ApplyStatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                break;


            case "injecting":
                ApplyStatusRing.Stroke = whiteBrush;
                ApplyStatusDot.Stroke = whiteBrush;
                ApplyStatusDot.Fill = dimBrush;
                ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSync20;
                ApplyStatusIcon.Foreground = whiteBrush;
                break;


            case "done":
                StopApplyPulse();
                ApplyStatusRing.Stroke = whiteBrush;
                ApplyStatusDot.Stroke = whiteBrush;
                ApplyStatusDot.Fill = dimBrush;
                ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                ApplyStatusIcon.Foreground = whiteBrush;
                break;


            case "error":
                StopApplyPulse();
                var redBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ApplyStatusRing.Stroke = redBrush;
                ApplyStatusDot.Stroke = redBrush;
                ApplyStatusDot.Fill = darkBrush;
                ApplyStatusIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle20;
                ApplyStatusIcon.Foreground = redBrush;
                break;
        }
    }


    private async void ApplyFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var flags = FFlagService.Instance.CurrentFlags;
            if (flags.Count == 0)
            {
                ConsoleService.Instance.Log("No FastFlags loaded. Import flags first.", "Warning");
                NotificationService.Instance.Show("No FastFlags loaded", NotificationType.Warning);
                return;
            }


            var flatDict = new Dictionary<string, string>();
            foreach (var f in flags)
                if (!string.IsNullOrWhiteSpace(f.Value))
                    flatDict[f.Name] = f.Value;


            string methodStr = SettingsService.Instance.Get<string>("InjectionMethod", "memory");
            var method = methodStr switch
            {
                "cache" => Combined.InjectionMethod.CacheMethod,
                "memory" => Combined.InjectionMethod.MemoryOffsets,
                "offsetless" => Combined.InjectionMethod.MemoryOffsetless,
                _ => Combined.InjectionMethod.Combined
            };


            bool needsAdmin = method != Combined.InjectionMethod.CacheMethod;
            if (needsAdmin && !CacheMethod.IsAdmin())
            {
                ConsoleService.Instance.Log("Administrator required for memory injection — relaunching as admin...", "Warning");
                NotificationService.Instance.Show("Relaunching as Administrator...", NotificationType.Warning, 3000);
                await Task.Delay(1000);
                CacheMethod.ElevateAndRelaunch();
                return;
            }


            string methodLabel = method switch
            {
                Combined.InjectionMethod.CacheMethod => "Cache Method",
                Combined.InjectionMethod.MemoryOffsets => "Memory Offsets",
                Combined.InjectionMethod.MemoryOffsetless => "Memory Offsetless",
                _ => "Combined"
            };


            ShowApplyOverlay(methodLabel);
            SetApplyPhase("waiting");
            SetApplyState("Waiting for Roblox...", "Searching for RobloxPlayerBeta process...", 0);


            int totalFlags = flatDict.Count;
            int injectedFlags = 0;


            Combined.Instance.OnStatusUpdate = (statusMsg, phase) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SetApplyPhase(phase);
                    switch (phase)
                    {
                        case "waiting":
                            SetApplyState("Waiting for Roblox...", statusMsg, 0);
                            break;
                        case "detecting":
                            SetApplyState("Detecting right moment...", statusMsg, 0.1);
                            break;
                        case "injecting":
                            SetApplyState("Injecting flags...", statusMsg, 0.3);
                            break;
                        case "done":
                            SetApplyState("Done!", statusMsg, 1.0);
                            break;
                        case "error":
                            SetApplyState("Failed", statusMsg, 1.0);
                            break;
                    }
                }));
            };


            var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            progressTimer.Tick += (s, ev) =>
            {
                if (injectedFlags < totalFlags && injectedFlags > 0)
                {
                    double fraction = (double)injectedFlags / totalFlags;
                    string phase = fraction >= 1.0 ? "done" : "injecting";
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetApplyState($"Injecting flags... ({injectedFlags}/{totalFlags})", $"{injectedFlags} of {totalFlags} flags written to memory", 0.3 + (fraction * 0.7));
                        if (phase == "done")
                            SetApplyPhase("done");
                    }));
                }
            };
            progressTimer.Start();


            ConsoleService.Instance.Log($"Applying {flatDict.Count} flags via {method}...", "Info");


            var result = await Combined.Instance.InjectAsync(flatDict, method, (done, total) =>
            {
                injectedFlags = done;
                if (total > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        double fraction = (double)done / total;
                        SetApplyState($"Injecting flags... ({done}/{total})", $"{done} of {total} flags written to memory", 0.3 + (fraction * 0.7));
                    }));
                }
            });


            progressTimer.Stop();


            _fflagsInjectedCount = result.ok ? flatDict.Count : 0;
            UpdateStatus();


            if (result.ok)
            {
                SetApplyPhase("done");
                SetApplyState("Flags Injected!", result.msg, 1.0);


                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                ApplyResultText.Text = $"{flatDict.Count:N0} FastFlags applied successfully";


                ConsoleService.Instance.Log($"Applied {flatDict.Count:N0} FastFlags ({result.msg})", "Success");
            }
            else
            {
                SetApplyPhase("error");
                SetApplyState("Injection Failed", result.msg, 1.0);


                ApplyResultBorder.Visibility = Visibility.Visible;
                ApplyResultBorder.Background = new SolidColorBrush(Color.FromArgb(20, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0x44, 0x44, 0x44));
                ApplyResultBorder.BorderThickness = new Thickness(1);
                ApplyResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.DismissCircle20;
                ApplyResultIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
                ApplyResultText.Text = result.msg;


                ConsoleService.Instance.Log($"Apply failed: {result.msg}", "Error");
            }


            await Task.Delay(2200);
            HideApplyOverlay();
            Combined.Instance.OnStatusUpdate = null;
        }
        catch (Exception ex)
        {
            HideApplyOverlay();
            Combined.Instance.OnStatusUpdate = null;
            ConsoleService.Instance.Log($"Apply error: {ex.Message}", "Error");
            NotificationService.Instance.Show($"Apply error: {ex.Message}", NotificationType.Error);
        }
    }


    private void UnapplyFlags_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConsoleService.Instance.Log("Unapply: Stopping all injection methods...", "Info");

            MemoryOffsets.Instance.StopWatchdog();
            MemoryOffsetless.Instance.Detach();
            try { CacheMethod.Instance.Stop(); } catch { }
            try { ProxyService.Instance.Stop(); } catch { }

            ProxyService.ForceCleanup();

            _fflagsInjectedCount = 0;
            UpdateStatus();

            string msg = "All injection stopped, hosts cleaned, certs removed, cache deleted, DNS flushed";
            ConsoleService.Instance.Log($"Unapply: {msg}", "Success");
            NotificationService.Instance.Show(msg, NotificationType.Success, 3000);
        }
        catch (Exception ex) { ConsoleService.Instance.Log($"Unapply error: {ex.Message}", "Error"); }
    }
}
