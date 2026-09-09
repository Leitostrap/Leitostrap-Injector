using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LeitostrapV7.Core;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Button = System.Windows.Controls.Button;


namespace LeitostrapV7.Views
{
    public partial class ProfilesView : UserControl
    {
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly FFlagService _flagService = FFlagService.Instance;
        private readonly GameFflagsService _gameService = GameFflagsService.Instance;
        private List<GameInfo> _allGames = new();
        private GameInfo? _previewGame;


        public ProfilesView()
        {
            InitializeComponent();
            LoadGames();
        }


        private void LoadGames()
        {
            GamesPanel.Items.Clear();
            _allGames = _gameService.Games.ToList();


            foreach (var game in _allGames)
                GamesPanel.Items.Add(CreateGameCard(game));


            CountText.Text = $"{_allGames.Count} games";
            StatusText.Text = "Ready";
        }


        private Border CreateGameCard(GameInfo game)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                ClipToBounds = true
            };


            var outerStack = new StackPanel();


            var imageBorder = new Border
            {
                Height = 160,
                Background = new SolidColorBrush(Color.FromRgb(8, 8, 8)),
                ClipToBounds = true,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };


            if (!string.IsNullOrEmpty(game.ImagePath) && File.Exists(game.ImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(Path.GetFullPath(game.ImagePath));
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 300;
                    bitmap.EndInit();
                    bitmap.Freeze();


                    var image = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    imageBorder.Child = image;
                }
                catch
                {
                    imageBorder.Child = CreatePlaceholderIcon(game.Name);
                }
            }
            else
            {
                imageBorder.Child = CreatePlaceholderIcon(game.Name);
            }


            outerStack.Children.Add(imageBorder);


            var infoStack = new StackPanel
            {
                Margin = new Thickness(8, 4, 8, 6)
            };


            infoStack.Children.Add(new TextBlock
            {
                Text = game.Name,
                FontSize = 11,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            });


            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
            };
            badge.Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{game.FFlagCount} flags",
                        FontSize = 8.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85))
                    }
                }
            };
            infoStack.Children.Add(badge);


            var btnGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });


            var previewBtn = CreateButtonWithIcon("Preview", game, "#0A0A0A", "#1A1A1A", new SolidColorBrush(Color.FromRgb(68, 68, 68)), "Eye20");
            previewBtn.Click += (s, e) => ShowPreview(game);
            previewBtn.Margin = new Thickness(0);
            previewBtn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(previewBtn, 0);


            var addBtn = CreateButtonWithIcon("Add", game, "#0B2E1A", "#1A1A1A", new SolidColorBrush(Color.FromRgb(34, 197, 94)), "Add20");
            addBtn.Click += (s, e) => AddGameFlags(game, false);
            addBtn.Margin = new Thickness(0);
            addBtn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(addBtn, 1);


            btnGrid.Children.Add(previewBtn);
            btnGrid.Children.Add(addBtn);
            infoStack.Children.Add(btnGrid);


            outerStack.Children.Add(infoStack);
            card.Child = outerStack;


            card.MouseEnter += (s, e) =>
            {
                card.BorderBrush = new SolidColorBrush(Colors.White);
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1, 1.02, TimeSpan.FromMilliseconds(200));
                card.RenderTransform = new System.Windows.Media.ScaleTransform(1, 1);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                card.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                card.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleX);
            };
            card.MouseLeave += (s, e) =>
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
                card.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                card.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleX);
            };


            return card;
        }


        private Border CreatePlaceholderIcon(string name)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };


            var initial = new TextBlock
            {
                Text = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(initial);


            return new Border
            {
                Child = stack,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }


        private Button CreateButton(string text, GameInfo game, string bg, string border, Brush foreground)
        {
            var btn = new Button
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                Foreground = foreground,
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Template = CreateModernButtonTemplate(bg, border, foreground)
            };


            var scale = new System.Windows.Media.ScaleTransform(1, 1);
            btn.RenderTransform = scale;
            btn.RenderTransformOrigin = new Point(0.5, 0.5);


            btn.MouseEnter += (s, e) =>
            {
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(150));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleX);
            };
            btn.MouseLeave += (s, e) =>
            {
                var scaleX = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleX);
            };


            return btn;
        }


        private ControlTemplate CreateModernButtonTemplate(string bgColor, string borderColor, Brush foreground)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderEl = new FrameworkElementFactory(typeof(Border));
            borderEl.Name = "border";
            borderEl.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor)));
            borderEl.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor)));
            borderEl.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderEl.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderEl.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));


            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderEl.AppendChild(contentPresenter);


            template.VisualTree = borderEl;


            var triggerMouseOver = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            triggerMouseOver.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(17, 17, 17)), "border"));


            var triggerPressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            triggerPressed.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(5, 5, 5)), "border"));


            template.Triggers.Add(triggerMouseOver);
            template.Triggers.Add(triggerPressed);


            return template;
        }


        private Button CreateButtonWithIcon(string text, GameInfo game, string bg, string border, Brush foreground, string iconSymbol)
        {
            var btn = CreateButton(text, game, bg, border, foreground);


            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };


            var icon = new SymbolIcon
            {
                Symbol = ParseSymbol(iconSymbol),
                FontSize = 10,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };


            var label = new TextBlock
            {
                Text = text,
                FontSize = 9.5,
                VerticalAlignment = VerticalAlignment.Center
            };


            stack.Children.Add(icon);
            stack.Children.Add(label);
            btn.Content = stack;


            return btn;
        }


        private SymbolRegular ParseSymbol(string name) => name switch
        {
            "Eye20" => SymbolRegular.Eye20,
            "Add20" => SymbolRegular.Add20,
            "PuzzlePiece20" => SymbolRegular.PuzzlePiece20,
            "ArrowSync20" => SymbolRegular.ArrowSync20,
            _ => SymbolRegular.Eye20
        };


        private void AddGameFlags(GameInfo game, bool append)
        {
            if (!append)
            {
                _flagService.CurrentFlags.Clear();
            }


            foreach (var kvp in game.FFlags)
            {
                var val = kvp.Value;
                string valueStr = val.ValueKind == JsonValueKind.String
                    ? val.GetString() ?? ""
                    : val.GetRawText();
                _flagService.AddFlag(kvp.Key, valueStr, "String");
            }


            _console.Log($"Added {game.FFlagCount} flags from \"{game.Name}\"", "Success");
            StatusText.Text = $"Added {game.FFlagCount} flags from {game.Name}";
        }


        private void ShowPreview(GameInfo game)
        {
            _previewGame = game;
            PreviewGameName.Text = game.Name;
            PreviewFlagCount.Text = $"{game.FFlagCount} flags";


            var preview = game.FFlags.Take(30).ToDictionary(k => k.Key, v =>
            {
                var val = v.Value;
                return val.ValueKind == JsonValueKind.String
                    ? (object)(val.GetString() ?? "")
                    : val.GetRawText();
            });


            PreviewJsonBox.Text = JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true });
            if (game.FFlagCount > 30)
                PreviewJsonBox.Text += $"\n\n... and {game.FFlagCount - 30} more flags";


            PreviewOverlay.Visibility = Visibility.Visible;
            PreviewContainer.Opacity = 0;
            PreviewContainer.RenderTransform = new System.Windows.Media.ScaleTransform(0.9, 0.9);
            PreviewContainer.RenderTransformOrigin = new Point(0.5, 0.5);


            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            var scaleIn = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(250));
            PreviewContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            PreviewContainer.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleIn);
            PreviewContainer.RenderTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleIn);
        }


        private void ClosePreview_Click(object sender, MouseButtonEventArgs e)
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
            _previewGame = null;
        }


        private void ClosePreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
            _previewGame = null;
        }


        private void PreviewAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_previewGame != null)
            {
                AddGameFlags(_previewGame, false);
                PreviewOverlay.Visibility = Visibility.Collapsed;
            }
        }


        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLower();
            GamesPanel.Items.Clear();


            var filtered = string.IsNullOrEmpty(query)
                ? _allGames
                : _allGames.Where(g => g.Name.ToLower().Contains(query)).ToList();


            foreach (var game in filtered)
                GamesPanel.Items.Add(CreateGameCard(game));


            CountText.Text = $"{filtered.Count} games";
        }


    }
}
