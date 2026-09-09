using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using LeitostrapV7.Core;


namespace LeitostrapV7.Views
{
    public partial class ThemesView : UserControl
    {
        private readonly ThemeService _themeService = ThemeService.Instance;
        private readonly ConsoleService _console = ConsoleService.Instance;
        private List<StaticTheme> _allStatic = new();
        private List<AnimatedTheme> _allAnimated = new();
        private string? _activeThemeName;


        public ThemesView()
        {
            InitializeComponent();
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadThemes();
            SizeChanged += (_, _) => RebuildCards();
        }


        private void LoadThemes()
        {
            _allStatic = _themeService.StaticThemes.ToList();
            _allAnimated = _themeService.AnimatedThemes.ToList();
            ThemeCountText.Text = $"{_allStatic.Count} + {_allAnimated.Count}";
            BuildCards();
        }


        private void RebuildCards() => BuildCards();


        private void BuildCards()
        {
            double availableWidth = ActualWidth > 0 ? ActualWidth - 40 : 800;
            double cardMargin = 8;
            double minCardWidth = 150;
            int cols = Math.Max(1, (int)((availableWidth + cardMargin) / (minCardWidth + cardMargin)));
            double cardWidth = (availableWidth - (cols - 1) * cardMargin) / cols;


            BuildStaticCards(cardWidth);
            BuildAnimatedCards(cardWidth);
        }


        private void BuildStaticCards(double cardWidth)
        {
            StaticList.Items.Clear();
            double cardHeight = cardWidth * 0.72;
            double previewHeight = cardHeight - 32;


            foreach (var theme in _allStatic)
            {
                var card = CreateStaticCard(theme, cardWidth, cardHeight, previewHeight);
                StaticList.Items.Add(card);
            }
        }


        private void BuildAnimatedCards(double cardWidth)
        {
            AnimatedList.Items.Clear();
            double cardHeight = cardWidth * 0.72;
            double previewHeight = cardHeight - 32;


            foreach (var theme in _allAnimated)
            {
                var card = CreateAnimatedCard(theme, cardWidth, cardHeight, previewHeight);
                AnimatedList.Items.Add(card);
            }
        }


        private Border CreateStaticCard(StaticTheme theme, double w, double h, double previewH)
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(12, 12, 12));
            var bdrBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            var accentBrush = ParseBrush(theme.Accent);
            var accentColor = ((SolidColorBrush)accentBrush).Color;


            var card = new Border
            {
                Width = w,
                Height = h,
                Background = bgBrush,
                BorderBrush = bdrBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                Child = BuildStaticContent(theme, previewH, accentBrush)
            };


            var scale = new ScaleTransform(1, 1);
            card.RenderTransform = scale;
            card.RenderTransformOrigin = new Point(0.5, 0.5);


            card.MouseEnter += (s, e) =>
            {
                card.BorderBrush = accentBrush;
                card.Background = new SolidColorBrush(Color.FromArgb(20, accentColor.R, accentColor.G, accentColor.B));
                AnimateScale(scale, 1.04);
            };
            card.MouseLeave += (s, e) =>
            {
                card.BorderBrush = bdrBrush;
                card.Background = bgBrush;
                AnimateScale(scale, 1);
            };
            card.MouseLeftButtonDown += (s, e) => ApplyStaticTheme(theme);


            return card;
        }


        private StackPanel BuildStaticContent(StaticTheme theme, double previewH, Brush accentBrush)
        {
            var stack = new StackPanel();


            var preview = new Grid { Height = previewH, Background = ParseBrush(theme.BgMain) };
            preview.Children.Add(new Border { Height = 10, Background = ParseBrush(theme.BgNav), VerticalAlignment = VerticalAlignment.Top });


            var mid = new Grid { Margin = new Thickness(8, 14, 8, 0), Height = 26, VerticalAlignment = VerticalAlignment.Top };
            mid.Children.Add(new Border { Background = ParseBrush(theme.BgCard), BorderBrush = ParseBrush(theme.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
            mid.Children.Add(new Border { Height = 4, Width = 20, Background = accentBrush, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            preview.Children.Add(mid);


            preview.Children.Add(new Border { Height = 6, Background = ParseBrush(theme.BgHover), VerticalAlignment = VerticalAlignment.Bottom });
            stack.Children.Add(preview);


            stack.Children.Add(new TextBlock
            {
                Text = theme.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                Margin = new Thickness(8, 5, 8, 7),
                TextTrimming = TextTrimming.CharacterEllipsis
            });


            return stack;
        }


        private Border CreateAnimatedCard(AnimatedTheme theme, double w, double h, double previewH)
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(12, 12, 12));
            var bdrBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            var accentBrush = ParseBrush(theme.Accent);
            var accentColor = ((SolidColorBrush)accentBrush).Color;


            var card = new Border
            {
                Width = w,
                Height = h,
                Background = bgBrush,
                BorderBrush = bdrBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                ClipToBounds = true,
                Child = BuildAnimatedContent(theme, previewH, accentBrush, accentColor)
            };


            var scale = new ScaleTransform(1, 1);
            card.RenderTransform = scale;
            card.RenderTransformOrigin = new Point(0.5, 0.5);


            card.MouseEnter += (s, e) =>
            {
                card.BorderBrush = accentBrush;
                card.Background = new SolidColorBrush(Color.FromArgb(20, accentColor.R, accentColor.G, accentColor.B));
                AnimateScale(scale, 1.04);
            };
            card.MouseLeave += (s, e) =>
            {
                card.BorderBrush = bdrBrush;
                card.Background = bgBrush;
                AnimateScale(scale, 1);
            };
            card.MouseLeftButtonDown += (s, e) => ApplyAnimatedTheme(theme);


            return card;
        }


        private StackPanel BuildAnimatedContent(AnimatedTheme theme, double previewH, Brush accentBrush, Color accentColor)
        {
            var stack = new StackPanel();


            var preview = new Border { Height = previewH, Background = ParseGradientBrush(theme.Gradient) };


            var glow = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.3
            };
            var pulseAnim = new DoubleAnimation(0.15, 0.6, TimeSpan.FromMilliseconds(800 + new Random().Next(400)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            glow.BeginAnimation(UIElement.OpacityProperty, pulseAnim);


            preview.Child = new Grid
            {
                Children =
                {
                    glow,
                    new Border { Height = 3, Background = accentBrush, VerticalAlignment = VerticalAlignment.Bottom }
                }
            };
            stack.Children.Add(preview);


            var nameRow = new Grid { Margin = new Thickness(8, 5, 8, 7) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });


            var nameText = new TextBlock
            {
                Text = theme.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);


            var animTag = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, accentColor.R, accentColor.G, accentColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            animTag.Child = new TextBlock
            {
                Text = theme.Anim,
                FontSize = 7,
                Foreground = accentBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(animTag, 1);


            nameRow.Children.Add(nameText);
            nameRow.Children.Add(animTag);
            stack.Children.Add(nameRow);


            return stack;
        }


        private static void AnimateScale(ScaleTransform scale, double to)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }


        private void ApplyStaticTheme(StaticTheme theme)
        {
            _activeThemeName = theme.Name;
            ActiveThemeBar.Visibility = Visibility.Visible;
            ActiveThemeName.Text = theme.Name;


            ApplyColorToResource("Bg", theme.BgMain);
            ApplyColorToResource("BgCard", theme.BgCard);
            ApplyColorToResource("BgHover", theme.BgHover);
            ApplyColorToResource("BgNav", theme.BgNav);
            ApplyColorToResource("BgInput", theme.BgCard);
            ApplyColorToResource("BorderMain", theme.Border);
            ApplyColorToResource("BorderLight", theme.Border);
            ApplyColorToResource("Accent", theme.Accent);


            NotificationService.Instance.SetAccent(theme.Accent);
            MainWindow.Instance?.StopParticleAnimation();
            MainWindow.Instance?.ApplyThemeToMainWindow(theme.BgMain, theme.BgNav, theme.Border, theme.Accent, false);
            MainWindow.Instance?.ApplyThemeToNav(theme.Accent);
            PropagateThemeToViews(theme.BgMain);


            _console.Log($"Applied theme: {theme.Name}", "Success");
            NotificationService.Instance.Show($"Theme \"{theme.Name}\" applied", NotificationType.Success);
        }


        private void ApplyAnimatedTheme(AnimatedTheme theme)
        {
            _activeThemeName = theme.Name;
            ActiveThemeBar.Visibility = Visibility.Visible;
            ActiveThemeName.Text = theme.Name;


            var accent = theme.Accent;
            var colors = ParseGradientColors(theme.Gradient);


            Color bgBase = colors.Count > 0 ? colors[0] : Colors.Black;
            Color cardBase = colors.Count > 1 ? colors[1] : Color.FromRgb(0x11, 0x11, 0x11);
            Color hoverBase = colors.Count > 1 ? colors[1] : Color.FromRgb(0x1a, 0x1a, 0x1a);


            ApplyColorToResource("Bg", ColorToHex(bgBase));
            ApplyColorToResource("BgCard", ColorToHex(cardBase));
            ApplyColorToResource("BgHover", ColorToHex(hoverBase));
            ApplyColorToResource("BgNav", ColorToHex(bgBase));
            ApplyColorToResource("BgInput", ColorToHex(bgBase));
            ApplyColorToResource("BorderMain", ColorToHex(bgBase));
            ApplyColorToResource("BorderLight", ColorToHex(bgBase));
            ApplyColorToResource("Accent", accent);


            NotificationService.Instance.SetAccent(accent);
            MainWindow.Instance?.StartParticleAnimation(theme.Anim, theme.Accent);
            MainWindow.Instance?.ApplyThemeToMainWindow(
                ColorToHex(bgBase),
                ColorToHex(bgBase),
                ColorToHex(bgBase),
                accent,
                true);
            MainWindow.Instance?.ApplyThemeToNav(accent);
            PropagateThemeToViews(ColorToHex(bgBase));


            _console.Log($"Applied animated theme: {theme.Name}", "Success");
            NotificationService.Instance.Show($"Theme \"{theme.Name}\" applied", NotificationType.Success);
        }


        private static void ApplyColorToResource(string key, string hex)
        {
            try
            {
                if (Application.Current.TryFindResource(key) is SolidColorBrush brush)
                    brush.Color = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch { }
        }


        private static List<Color> ParseGradientColors(string css)
        {
            var colors = new List<Color>();
            var matches = System.Text.RegularExpressions.Regex.Matches(css, "#[0-9a-fA-F]{6}");
            foreach (System.Text.RegularExpressions.Match m in matches)
                colors.Add((Color)ColorConverter.ConvertFromString(m.Value));
            return colors;
        }


        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";


        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query))
            {
                BuildCards();
            }
            else
            {
                var filteredStatic = _allStatic.Where(t => t.Name.ToLower().Contains(query)).ToList();
                var filteredAnimated = _allAnimated.Where(t => t.Name.ToLower().Contains(query)).ToList();


                double availableWidth = ActualWidth > 0 ? ActualWidth - 40 : 800;
                double cardMargin = 8;
                double minCardWidth = 150;
                int cols = Math.Max(1, (int)((availableWidth + cardMargin) / (minCardWidth + cardMargin)));
                double cardWidth = (availableWidth - (cols - 1) * cardMargin) / cols;
                double cardHeight = cardWidth * 0.72;
                double previewHeight = cardHeight - 32;


                StaticList.Items.Clear();
                foreach (var theme in filteredStatic)
                    StaticList.Items.Add(CreateStaticCard(theme, cardWidth, cardHeight, previewHeight));


                AnimatedList.Items.Clear();
                foreach (var theme in filteredAnimated)
                    AnimatedList.Items.Add(CreateAnimatedCard(theme, cardWidth, cardHeight, previewHeight));
            }
        }


        private void ClearBgBtn_Click(object sender, RoutedEventArgs e)
        {
            _activeThemeName = null;
            ActiveThemeBar.Visibility = Visibility.Collapsed;


            ApplyColorToResource("Bg", "#080808");
            ApplyColorToResource("BgNav", "#0A0A0A");
            ApplyColorToResource("BgCard", "#111111");
            ApplyColorToResource("BgHover", "#1A1A1A");
            ApplyColorToResource("BgInput", "#0D0D0D");
            ApplyColorToResource("BorderMain", "#1A1A1A");
            ApplyColorToResource("BorderLight", "#222222");
            ApplyColorToResource("Accent", "#FFFFFF");


            NotificationService.Instance.SetAccent("#FFFFFF");
            MainWindow.Instance?.StopParticleAnimation();
            MainWindow.Instance?.ApplyThemeToMainWindow("#080808", "#0A0A0A", "#1A1A1A", "#FFFFFF", false);
            MainWindow.Instance?.ApplyThemeToNav("#FFFFFF");
            PropagateThemeToViews("#080808");


            _console.Log("Theme cleared, using default", "Info");
            NotificationService.Instance.Show("Theme cleared, using default", NotificationType.Info);
        }


        private void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.gif;*.bmp)|*.png;*.jpg;*.gif;*.bmp|All files (*.*)|*.*",
                Title = "Select Background Image"
            };
            if (dialog.ShowDialog() == true)
            {
                _console.Log($"Background image set: {Path.GetFileName(dialog.FileName)}", "Success");
                NotificationService.Instance.Show($"Background set: {Path.GetFileName(dialog.FileName)}", NotificationType.Success);
            }
        }


        private void PropagateThemeToViews(string bgHex)
        {
            var brush = ParseBrush(bgHex);
            var mw = MainWindow.Instance;
            if (mw == null) return;


            for (int i = 0; i < 12; i++)
            {
                var view = mw.GetView(i);
                if (view is UserControl uc)
                    uc.Background = brush;
            }
        }


        private static SolidColorBrush ParseBrush(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Black); }
        }


        private static LinearGradientBrush ParseGradientBrush(string css)
        {
            try
            {
                var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                if (css.Contains("radial"))
                {
                    brush.StartPoint = new Point(0.5, 0);
                    brush.EndPoint = new Point(0.5, 1);
                }


                var matches = System.Text.RegularExpressions.Regex.Matches(css, "#[0-9a-fA-F]{6}");
                var colors = new List<Color>();
                foreach (System.Text.RegularExpressions.Match m in matches)
                    colors.Add((Color)ColorConverter.ConvertFromString(m.Value));


                if (colors.Count >= 2)
                {
                    brush.GradientStops.Add(new GradientStop(colors[0], 0));
                    brush.GradientStops.Add(new GradientStop(colors[1], 1));
                }
                else if (colors.Count == 1)
                {
                    brush.GradientStops.Add(new GradientStop(colors[0], 0));
                    brush.GradientStops.Add(new GradientStop(colors[0], 1));
                }
                return brush;
            }
            catch
            {
                return new LinearGradientBrush(Colors.Black, Colors.DarkGray, 0);
            }
        }
    }
}
