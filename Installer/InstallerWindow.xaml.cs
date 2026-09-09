using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LeitostrapV7.Core;


namespace LeitostrapV7.Installer;


public class LanguageItem
{
    public string Flag { get; set; } = "";
    public string Name { get; set; } = "";
    public string Native { get; set; } = "";
    public string Code { get; set; } = "";
}


public partial class InstallerWindow : Window
{
    public event EventHandler? InstallCompleted;
    private int _currentPage;
    private string _selectedLanguage = "en";
    private string _installPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Leitostrap");
    private readonly UIElement[] _pages;


    private readonly List<LanguageItem> _languages = new()
    {
        new() { Flag = "🇺🇸", Name = "English", Native = "English", Code = "en" },
        new() { Flag = "🇪🇸", Name = "Spanish", Native = "Español", Code = "es" },
        new() { Flag = "🇧🇷", Name = "Portuguese", Native = "Português", Code = "pt" },
        new() { Flag = "🇫🇷", Name = "French", Native = "Français", Code = "fr" },
        new() { Flag = "🇩🇪", Name = "German", Native = "Deutsch", Code = "de" },
        new() { Flag = "🇮🇹", Name = "Italian", Native = "Italiano", Code = "it" },
        new() { Flag = "🇷🇺", Name = "Russian", Native = "Русский", Code = "ru" },
        new() { Flag = "🇯🇵", Name = "Japanese", Native = "日本語", Code = "ja" },
        new() { Flag = "🇰🇷", Name = "Korean", Native = "한국어", Code = "ko" },
        new() { Flag = "🇨🇳", Name = "Chinese (Simplified)", Native = "简体中文", Code = "zh" },
        new() { Flag = "🇹🇼", Name = "Chinese (Traditional)", Native = "繁體中文", Code = "zh-TW" },
        new() { Flag = "🇸🇦", Name = "Arabic", Native = "العربية", Code = "ar" },
        new() { Flag = "🇹🇷", Name = "Turkish", Native = "Türkçe", Code = "tr" },
        new() { Flag = "🇵🇱", Name = "Polish", Native = "Polski", Code = "pl" },
        new() { Flag = "🇳🇱", Name = "Dutch", Native = "Nederlands", Code = "nl" },
        new() { Flag = "🇸🇪", Name = "Swedish", Native = "Svenska", Code = "sv" },
        new() { Flag = "🇻🇳", Name = "Vietnamese", Native = "Tiếng Việt", Code = "vi" },
        new() { Flag = "🇮🇩", Name = "Indonesian", Native = "Bahasa Indonesia", Code = "id" },
        new() { Flag = "🇹🇭", Name = "Thai", Native = "ไทย", Code = "th" },
        new() { Flag = "🇺🇦", Name = "Ukrainian", Native = "Українська", Code = "uk" },
    };


    public InstallerWindow()
    {
        InitializeComponent();
        _pages = new UIElement[] { PageWelcome, PageLanguage, PagePath, PageInstalling, PageDone };
        LanguageList.ItemsSource = _languages;
        Loaded += (_, _) => Core.LanguageService.Instance.Apply(this);
    }


    private void ShowPage(int index)
    {
        foreach (var page in _pages)
            page.Visibility = Visibility.Collapsed;
        _pages[index].Visibility = Visibility.Visible;
        _currentPage = index;
        Core.LanguageService.Instance.Apply(this);
    }


    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();


    private void WelcomeContinue_Click(object sender, RoutedEventArgs e) => ShowPage(1);


    private void LangItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.Tag is LanguageItem lang)
        {
            _selectedLanguage = lang.Code;
            Core.LanguageService.Instance.Load(_selectedLanguage);
            Core.LanguageService.Instance.Apply(this);
            ShowPage(2);
        }
    }


    private void LangNext_Click(object sender, RoutedEventArgs e) => ShowPage(2);


    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0) ShowPage(_currentPage - 1);
    }


    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Installation Folder",
            InitialDirectory = _installPath
        };
        if (dialog.ShowDialog() == true)
        {
            _installPath = Path.Combine(dialog.FolderName, "Leitostrap");
            InstallPathText.Text = _installPath;
        }
    }


    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(3);
        var progress = InstallProgress;
        var statusText = InstallStatusText;


        double totalWidth = 500;


        try
        {
            statusText.Text = "Preparing installation...";
            progress.Width = 0;
            await Task.Delay(200);


            statusText.Text = "Copying files...";
            AnimateProgress(progress, totalWidth * 0.2, 400);
            await Task.Delay(400);


            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => statusText.Text = "Installing application...");
            });


            bool shortcuts = false, admin = false, antivirus = false;
            Dispatcher.Invoke(() =>
            {
                shortcuts = CreateShortcutsChk.IsChecked == true;
                admin = RunAsAdminChk.IsChecked == true;
                antivirus = ExcludeAvChk.IsChecked == true;
            });


            InstallerService.Install(_installPath, shortcuts, admin, antivirus, "7.0.0");


            SettingsService.Instance.Set("Installed", true);
            SettingsService.Instance.Set("InstallPath", _installPath);
            SettingsService.Instance.Set("InstallLanguage", _selectedLanguage);


            AnimateProgress(progress, totalWidth * 0.6, 500);
            await Task.Delay(200);


            Dispatcher.Invoke(() => statusText.Text = "Registering in Programs and Features...");
            AnimateProgress(progress, totalWidth * 0.8, 400);
            await Task.Delay(400);


            Dispatcher.Invoke(() => statusText.Text = "Finalizing...");
            AnimateProgress(progress, totalWidth, 300);
            await Task.Delay(500);


            ShowPage(4);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Installation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowPage(2);
        }
    }


    private void AnimateProgress(System.Windows.Controls.Border target, double to, int durationMs)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        target.BeginAnimation(WidthProperty, anim);
    }


    private void LaunchBtn_Click(object sender, RoutedEventArgs e)
    {
        InstallCompleted?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
