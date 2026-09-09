using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LeitostrapV7.Core;
using Microsoft.Win32;


namespace LeitostrapV7.Views
{
    public partial class VersionsView : UserControl
    {
        private readonly ProcessService _processService = ProcessService.Instance;
        private readonly SettingsService _settings = SettingsService.Instance;
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly System.Timers.Timer _refreshTimer;
        private string _currentVersion = "";


        public VersionsView()
        {
            InitializeComponent();
            _refreshTimer = new System.Timers.Timer(2000);
            _refreshTimer.Elapsed += (s, e) => Dispatcher.BeginInvoke(new Action(RefreshStatus));
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
            Loaded += OnLoaded;
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
            LoadSavedPath();
        }


        private void LoadSavedPath()
        {
            try
            {
                string saved = _settings.Get<string>("RobloxExePath") ?? "";
                if (string.IsNullOrEmpty(saved))
                    saved = _settings.Get<string>("roblox_exe_path") ?? "";
                if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                {
                    BrowsePathText.Text = saved;
                    BrowsePathText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                }
            }
            catch { }
        }


        private void RefreshStatus()
        {
            try
            {
                bool running = _processService.IsRunning();
                string version = _processService.GetRobloxVersion();
                string exePath = _processService.FindRobloxExe();
                bool installed = !string.IsNullOrEmpty(exePath);
                var installedVersions = _processService.GetInstalledVersions();


                Dispatcher.BeginInvoke(new Action(() =>
                {


                    string displayVersion = version == "Unknown" ? "Checking..." : version;
                    DownloadVersionText.Text = displayVersion;


                    DownloadLaunchBtn.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;


                    if (!string.IsNullOrEmpty(exePath))
                    {
                        BrowsePathText.Text = exePath;
                        BrowsePathText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                    }


                    InstalledVersionsList.ItemsSource = installedVersions;
                    NoVersionsText.Visibility = installedVersions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;


                    _currentVersion = version;
                }));
            }
            catch { }
        }


        private void LaunchBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exe = _processService.FindRobloxExe();
                if (string.IsNullOrEmpty(exe))
                {
                    NotificationService.Instance.Show("Roblox not found. Install or browse for it.", NotificationType.Warning);
                    return;
                }


                _console.Log("Launching Roblox...", "Info");
                NotificationService.Instance.Show("Launching Roblox...", NotificationType.Info, 2000);


                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                });


                RefreshStatus();
            }
            catch (Exception ex)
            {
                _console.Log($"Failed to launch: {ex.Message}", "Error");
                NotificationService.Instance.Show($"Failed to launch: {ex.Message}", NotificationType.Error);
            }
        }


        private void VersionLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string versionFolder)
            {
                try
                {
                    string exePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Roblox", "Versions", versionFolder, "RobloxPlayerBeta.exe");


                    if (!File.Exists(exePath))
                    {
                        NotificationService.Instance.Show("RobloxPlayerBeta.exe not found in this version", NotificationType.Warning);
                        return;
                    }


                    _settings.Set("RobloxExePath", exePath);
                    _settings.Set("roblox_exe_path", exePath);


                    _console.Log($"Launching {versionFolder}...", "Info");
                    NotificationService.Instance.Show($"Selected: {versionFolder}", NotificationType.Success, 2000);


                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Normal
                    });
                }
                catch (Exception ex)
                {
                    _console.Log($"Failed to launch: {ex.Message}", "Error");
                }
            }
        }


        private void VersionUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string versionFolder)
            {
                try
                {
                    var result = MessageBox.Show(
                        $"Uninstall {versionFolder}?\n\nThis will delete the entire version folder.",
                        "Uninstall Roblox",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);


                    if (result != MessageBoxResult.Yes) return;


                    bool success = _processService.UninstallVersion(versionFolder);
                    if (success)
                    {
                        _console.Log($"Uninstalled {versionFolder}", "Warning");
                        NotificationService.Instance.Show($"Uninstalled {versionFolder}", NotificationType.Warning, 2000);
                        RefreshStatus();
                    }
                    else
                    {
                        NotificationService.Instance.Show("Failed to uninstall — run as Administrator", NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    _console.Log($"Uninstall error: {ex.Message}", "Error");
                }
            }
        }


        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Roblox Player",
                    Filter = "Roblox Player|RobloxPlayerBeta.exe|All Executables|*.exe",
                    FileName = "RobloxPlayerBeta.exe"
                };


                if (dialog.ShowDialog() == true)
                {
                    string path = dialog.FileName;
                    _settings.Set("RobloxExePath", path);
                    _settings.Set("roblox_exe_path", path);
                    BrowsePathText.Text = path;
                    BrowsePathText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));


                    _console.Log($"Roblox path set: {path}", "Success");
                    NotificationService.Instance.Show("Roblox path updated", NotificationType.Success, 2000);
                    RefreshStatus();
                }
            }
            catch (Exception ex)
            {
                _console.Log($"Browse error: {ex.Message}", "Error");
                NotificationService.Instance.Show($"Browse error: {ex.Message}", NotificationType.Error);
            }
        }


        private void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _console.Log("Opening Roblox download page...", "Info");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.roblox.com/download",
                    UseShellExecute = true
                });
                NotificationService.Instance.Show("Opening Roblox download page...", NotificationType.Info, 3000);
            }
            catch (Exception ex)
            {
                _console.Log($"Failed to open download page: {ex.Message}", "Error");
            }
        }


        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rotate = new RotateTransform(0);
                RefreshIcon.RenderTransform = rotate;
                RefreshIcon.RenderTransformOrigin = new Point(0.5, 0.5);
                var spin = new System.Windows.Media.Animation.DoubleAnimation(0, 360,
                    TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                rotate.BeginAnimation(RotateTransform.AngleProperty, spin);


                RefreshStatus();
                _console.Log("Version info refreshed", "Info");
                NotificationService.Instance.Show("Version info refreshed", NotificationType.Info, 2000);
            }
            catch (Exception ex)
            {
                _console.Log($"Refresh error: {ex.Message}", "Error");
            }
        }
    }
}
