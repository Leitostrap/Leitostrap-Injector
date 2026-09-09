using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LeitostrapV7.Core;
using System.Media;


namespace LeitostrapV7.Views;


public partial class MainWindow : Window
{
    private readonly object[] _views = new object[12];
    public static MainWindow? Instance { get; private set; }
    private bool _sidebarExpanded = false;
    private bool _reallyClose;


    private DispatcherTimer? _particleTimer;
    private readonly List<Particle> _particles = new();
    private bool _particleActive;


    private DispatcherTimer? _tipsTimer;
    private int _tipIndex;
    private readonly string[] _tips =
    {
        "Tip: Use Memory Offsets for best performance",
        "Tip: Import FFlags from JSON files for quick setup",
        "Tip: Enable Smart Mode for auto-injection",
        "Tip: Use the FFlags Editor to fine-tune settings",
        "Tip: Create Profiles to save your favorite configurations",
        "Tip: Check the Offsets DataBase for the latest offsets",
        "Tip: Use Cache Method if Memory Offsets don't work",
        "Tip: Enable Auto Apply to inject on Roblox launch",
        "Tip: Use the Console to debug injection issues",
        "Tip: Toggle UI with the Insert key for quick access"
    };


    private class Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Radius;
        public Brush Brush;
    }


    public void StartParticleAnimation(string animType, string accentColor)
    {
        ParticleCanvas.Visibility = Visibility.Visible;
        ParticleCanvas.Background = Brushes.Transparent;
        _particles.Clear();
        _particleActive = true;
        _particleTimer?.Stop();


        var rng = new Random();
        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentColor));
        accentBrush.Freeze();
        var dimBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        dimBrush.Freeze();


        double w = ActualWidth > 0 ? ActualWidth : 1050;
        double h = ActualHeight > 0 ? ActualHeight : 600;


        for (int i = 0; i < 55; i++)
        {
            _particles.Add(new Particle
            {
                X = rng.NextDouble() * w,
                Y = rng.NextDouble() * h,
                Vx = (rng.NextDouble() - 0.5) * 0.45,
                Vy = (rng.NextDouble() - 0.5) * 0.45,
                Radius = 0.8 + rng.NextDouble() * 1.8,
                Brush = rng.NextDouble() < 0.4 ? accentBrush : dimBrush
            });
        }


        _particleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _particleTimer.Tick += (_, _) => ParticleTick();
        _particleTimer.Start();
    }


    private void ParticleTick()
    {
        if (!_particleActive) return;


        double w = ParticleCanvas.ActualWidth > 0 ? ParticleCanvas.ActualWidth : 1050;
        double h = ParticleCanvas.ActualHeight > 0 ? ParticleCanvas.ActualHeight : 600;


        foreach (var p in _particles)
        {
            p.X += p.Vx;
            p.Y += p.Vy;
            if (p.X < 0 || p.X > w) p.Vx *= -1;
            if (p.Y < 0 || p.Y > h) p.Vy *= -1;
            p.X = Math.Clamp(p.X, 0, w);
            p.Y = Math.Clamp(p.Y, 0, h);
        }


        ParticleCanvas.Children.Clear();
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                var a = _particles[i];
                dc.DrawEllipse(a.Brush, null, new Point(a.X, a.Y), a.Radius, a.Radius);
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    var b = _particles[j];
                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 120)
                    {
                        double alpha = (1.0 - dist / 120.0) * 0.15;
                        var lineBrush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 255, 255));
                        dc.DrawLine(new Pen(lineBrush, 0.5), new Point(a.X, a.Y), new Point(b.X, b.Y));
                    }
                }
            }
        }


        var image = new System.Windows.Controls.Image();
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        image.Source = rtb;
        ParticleCanvas.Children.Add(image);
    }


    public void StopParticleAnimation()
    {
        _particleActive = false;
        _particleTimer?.Stop();
        _particleTimer = null;
        ParticleCanvas.Children.Clear();
        ParticleCanvas.Visibility = Visibility.Collapsed;
    }


    public void ApplyThemeToMainWindow(string bgHex, string navHex, string borderHex, string accentHex, bool isAnimated)
    {
        try
        {
            var bg = ParseBrush(bgHex);
            var nav = ParseBrush(navHex);
            var border = ParseBrush(borderHex);


            if (isAnimated)
            {
                var solidBg = ParseBrush(bgHex);
                if (solidBg.Color.A < 255)
                    solidBg.Color = Color.FromArgb(255, solidBg.Color.R, solidBg.Color.G, solidBg.Color.B);


                RootContent.Background = solidBg;
                SidebarBorder.Background = solidBg;
                SidebarBorder.BorderBrush = border;
                TitleBar.Background = solidBg;
                ContentBorder.Background = solidBg;
                ContentBorder.Opacity = 1.0;
            }
            else
            {
                RootContent.Background = bg;
                SidebarBorder.Background = nav;
                SidebarBorder.BorderBrush = border;
                TitleBar.Background = nav;
                ContentBorder.Background = bg;
                ContentBorder.Opacity = 1.0;
            }
        }
        catch { }
    }


    public void ApplyThemeToNav(string accentHex)
    {
        try
        {
            var accentBrush = ParseBrush(accentHex);
            var navs = new[] { NavHome, NavFFlags, NavOffsets, NavProfiles, NavCommunityFflags, NavConsole, NavClients, NavVersions, NavThemes, NavSettings, NavAbout };
            foreach (var rb in navs)
            {
                rb.Foreground = accentBrush;
                rb.ApplyTemplate();
                if (rb.Template?.FindName("Bar", rb) is System.Windows.Controls.Border bar)
                    bar.Background = accentBrush;
            }
        }
        catch { }
    }


    private static SolidColorBrush ParseBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.Black); }
    }


    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        _views[0] = new HomeView();
        _views[1] = new FFlagsEditorView();
        _views[2] = new OffsetsView();
        _views[3] = new ProfilesView();
        _views[4] = new ConsoleView();
        _views[5] = new RobloxClientsView();
        _views[6] = new VersionsView();
        _views[7] = new ThemesView();
        _views[8] = new SettingsView();
        _views[9] = new AboutView();
        _views[10] = new ReadDocsView();
        _views[11] = new CommunityFflagsView();
        ContentArea.Content = _views[0];
        NotificationService.Instance.SetHost(ToastHost);


        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        SizeChanged += (_, _) => UpdateClipGeometry();
    }


    private void UpdateClipGeometry()
    {
        if (ClipGeometry != null)
            ClipGeometry.Rect = new Rect(0, 0, ClipBorder.ActualWidth, ClipBorder.ActualHeight);
    }


    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);


    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmExtendFrameIntoClientArea(
        IntPtr hwnd, ref MARGINS margins);


    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Top, Right, Bottom; }


    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_DONOTROUND = 1;


    private void ApplyDwmRounding()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;


            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));


            var margins = new MARGINS { Left = 1, Top = 1, Right = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { }
    }


    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyDwmRounding();
            UpdateClipGeometry();
            SetNavLabels();
            ToggleSidebarLabels(false);
            Core.LanguageService.Instance.Apply(this);
            StartShimmer(NeonTitle, 3);
            StartLogoPulse();
            PlayOpenAnimation();
            Core.DiscordService.Instance.Init();
            await RunBootSequence();
            StartTipsTimer();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "leitostrap_crash.log"),
                $"[{DateTime.Now}] BOOT CRASH: {ex}\n{ex.StackTrace}\n\n"); } catch { }
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RootContent.Effect = null;
        }
    }


    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };


        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
        this.BeginAnimation(OpacityProperty, fade);


        var sx = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
        var sy = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
    }


    private void StartTipsTimer()
    {
        bool tipsEnabled = Core.SettingsService.Instance.Get<bool>("tips_enabled", false);
        if (!tipsEnabled) return;


        _tipsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _tipsTimer.Tick += (_, _) => ShowNextTip();
        _tipsTimer.Start();
    }


    private void ShowNextTip()
    {
        bool tipsEnabled = Core.SettingsService.Instance.Get<bool>("tips_enabled", false);
        if (!tipsEnabled)
        {
            _tipsTimer?.Stop();
            return;
        }


        string tip = _tips[_tipIndex % _tips.Length];
        _tipIndex++;


        TipText.Text = tip;
        TipBanner.Visibility = Visibility.Visible;
        TipBanner.Opacity = 0;


        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(-20, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };


        TipBanner.RenderTransform = new TranslateTransform();
        TipBanner.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        TipBanner.BeginAnimation(OpacityProperty, fadeIn);


        PlayNotificationSound();


        DispatcherTimer? hideTimer = null;
        hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();


            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                TipBanner.Visibility = Visibility.Collapsed;
            };
            TipBanner.BeginAnimation(OpacityProperty, fadeOut);
        };
        hideTimer.Start();
    }


    private void PlayNotificationSound()
    {
        bool soundEnabled = Core.SettingsService.Instance.Get<bool>("notif_sound", false);
        bool uiSoundsEnabled = Core.SettingsService.Instance.Get<bool>("ui_sounds", false);
        if (!soundEnabled && !uiSoundsEnabled) return;


        try
        {
            var player = new System.Media.SoundPlayer();
            using var ms = GenerateBeepWav(800, 150);
            player.Stream = ms;
            player.Play();
        }
        catch { }
    }


    private MemoryStream GenerateBeepWav(int freq, int durationMs)
    {
        int sampleRate = 44100;
        int samples = sampleRate * durationMs / 1000;
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        int dataSize = samples * 2;
        writer.Write(new char[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + dataSize);
        writer.Write(new char[] { 'W', 'A', 'V', 'E' });
        writer.Write(new char[] { 'f', 'm', 't', ' ' });
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(new char[] { 'd', 'a', 't', 'a' });
        writer.Write(dataSize);
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(Math.Sin(2 * Math.PI * freq * t) * 8000 * Math.Exp(-3.0 * i / samples));
            writer.Write(sample);
        }
        ms.Position = 0;
        return ms;
    }


    private async Task RunBootSequence()
    {
        var L = Core.LanguageService.Instance.L;
        string[] steps = { L("Loading UI"), L("Loading FFlags"), L("Loading Offsets"), L("Loading Games FFlags"), L("Loading Console") };
        const double trackWidth = 260;


        await Task.Delay(250);


        for (int i = 0; i < steps.Length; i++)
        {
            LoadingStatus.Text = steps[i] + "...";
            double target = Math.Round((i + 1) * trackWidth / steps.Length);
            var w = new DoubleAnimation(target, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LoadingBarFill.BeginAnimation(WidthProperty, w);


            if (i == 2 && !OffsetService.Instance.IsFetched)
            {
                try { await Task.WhenAny(OffsetService.Instance.FetchOffsetsAsync(), Task.Delay(5000)); }
                catch { }
            }


            await Task.Delay(340);
        }


        LoadingStatus.Text = "Ready";
        LoadingStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));


        var pop = new DoubleAnimation(1.07, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        LogoPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        LogoPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pop);


        await Task.Delay(450);


        var overlayFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        LoadingOverlay.BeginAnimation(OpacityProperty, overlayFade);


        if (RootContent.Effect is BlurEffect blur)
        {
            blur.BeginAnimation(BlurEffect.RadiusProperty,
                new DoubleAnimation(blur.Radius, 0, TimeSpan.FromMilliseconds(450)));
        }


        await Task.Delay(460);
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RootContent.Effect = null;
    }


    private void StartShimmer(TextBlock target, double seconds)
    {
        if (target?.Foreground is LinearGradientBrush originalBrush)
        {
            var brush = originalBrush.Clone();
            var transform = new TranslateTransform();
            brush.Transform = transform;
            target.Foreground = brush;


            var anim = new DoubleAnimation
            {
                From = -1,
                To = 1,
                Duration = new Duration(TimeSpan.FromSeconds(seconds)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            transform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }


    private void StartLogoPulse()
    {
        var pulse = new DoubleAnimation(1, 1.07, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LogoPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        LogoPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }


    private void SetNavLabels()
    {
        SetLabel(NavHome, "Home");
        SetLabel(NavFFlags, "FFlags Editor");
        SetLabel(NavOffsets, "Offsets DataBase");
        SetLabel(NavProfiles, "Games FFlags");
        SetLabel(NavCommunityFflags, "Community FFlags");
        SetLabel(NavConsole, "Console");
        SetLabel(NavClients, "Roblox Clients");
        SetLabel(NavVersions, "Roblox Versions");
        SetLabel(NavThemes, "Themes");
        SetLabel(NavSettings, "Settings");
        SetLabel(NavAbout, "About");
    }


    private static void SetLabel(RadioButton rb, string text)
    {
        rb.ApplyTemplate();
        if (rb.Template?.FindName("Label", rb) is TextBlock label)
        {
            label.Text = text;
        }
    }


    public void ToggleSidebarLabels(bool show)
    {
        var navs = new[] { NavHome, NavFFlags, NavOffsets, NavProfiles, NavCommunityFflags, NavConsole, NavClients, NavVersions, NavThemes, NavSettings, NavAbout };
        foreach (var rb in navs)
        {
            rb.ApplyTemplate();
            if (rb.Template?.FindName("Label", rb) is TextBlock label)
            {
                var anim = new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(200));
                anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
                label.BeginAnimation(OpacityProperty, anim);
            }
        }


        if (NeonTitle != null)
            NeonTitle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }


    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;


        double from = _sidebarExpanded ? 48 : 180;
        double to = _sidebarExpanded ? 180 : 48;


        var widthAnim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(250));
        widthAnim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
        SidebarBorder.BeginAnimation(WidthProperty, widthAnim);


        ToggleSidebarLabels(_sidebarExpanded);


        if (ToggleArrow != null)
        {
            ToggleArrow.Symbol = _sidebarExpanded
                ? Wpf.Ui.Controls.SymbolRegular.ArrowLeft20
                : Wpf.Ui.Controls.SymbolRegular.ArrowRight20;
        }
    }


    public object? GetView(int index) => index >= 0 && index < _views.Length ? _views[index] : null;


    public void NavigateTo(int index)
    {
        if (index >= 0 && index < _views.Length && _views[index] != null)
        {
            AnimationHelper.AnimateContentSwitch(ContentArea, _views[index]);


            if (_views[index] is FrameworkElement fe)
            {
                void OnViewLoaded(object s, RoutedEventArgs re)
                {
                    fe.Loaded -= OnViewLoaded;
                    Core.LanguageService.Instance.Apply(this);
                }
                fe.Loaded += OnViewLoaded;
            }


            Dispatcher.BeginInvoke(new Action(() =>
            {
                Core.LanguageService.Instance.Apply(this);
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }


    public void OpenConsoleLog()
    {
        if (GetView(4) is ConsoleView cv)
        {
            cv.RefreshLog();
        }
    }


    public void UpdateStatus(string text, bool isActive)
    {
    }


    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }


    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            this.BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            WindowState = WindowState.Minimized;
        };
        this.BeginAnimation(OpacityProperty, fade);
    }


    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            this.BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }
    }


    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();


    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _tipsTimer?.Stop();
        if (_reallyClose || WindowState == WindowState.Minimized) return;


        e.Cancel = true;


        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
        var sx = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
        var sy = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };


        fade.Completed += (_, _) =>
        {
            _reallyClose = true;
            Close();
        };


        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        this.BeginAnimation(OpacityProperty, fade);
    }


    private bool _isMaximized;
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;


    private void ToggleMaximize()
    {
        bool maximizing = !_isMaximized;
        _isMaximized = maximizing;


        if (MaximizeIcon != null)
        {
            MaximizeIcon.Symbol = maximizing
                ? Wpf.Ui.Controls.SymbolRegular.SquareMultiple20
                : Wpf.Ui.Controls.SymbolRegular.Maximize20;


            var pop = new ScaleTransform(0.6, 0.6);
            MaximizeIcon.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            MaximizeIcon.RenderTransform = pop;
            var popAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);
        }


        if (ClipBorder != null)
            ClipBorder.CornerRadius = maximizing ? new CornerRadius(0) : new CornerRadius(8);


        if (maximizing)
        {
            _restoreLeft = Left;
            _restoreTop = Top;
            _restoreWidth = Width;
            _restoreHeight = Height;


            var screen = SystemParameters.WorkArea;
            Left = screen.Left;
            Top = screen.Top;
            Width = screen.Width;
            Height = screen.Height;
        }
        else
        {
            Left = _restoreLeft;
            Top = _restoreTop;
            Width = _restoreWidth;
            Height = _restoreHeight;
        }


        double from = maximizing ? 0.96 : 1.04;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sx = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var sy = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);


        var oc = new DoubleAnimation(maximizing ? 0.8 : 0.9, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        RootContent.BeginAnimation(OpacityProperty, oc);
    }


    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null || sender is not RadioButton rb || rb.Tag is not string tag) return;
        if (int.TryParse(tag, out int idx))
        {
            NavigateTo(idx);
            Core.DiscordService.Instance.SetSection(idx);
        }
    }
}
