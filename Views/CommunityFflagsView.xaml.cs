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


namespace LeitostrapV7.Views
{
    public partial class CommunityFflagsView : UserControl
    {
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly FFlagService _flagService = FFlagService.Instance;
        private readonly CommunityFflagsService _communityService = CommunityFflagsService.Instance;
        private List<CommunityFflagEntry> _allCommunity = new();


        public CommunityFflagsView()
        {
            InitializeComponent();
            _ = LoadCommunityFflagsAsync();
        }


        private async Task LoadCommunityFflagsAsync()
        {
            CommunityStatusText.Text = "Loading...";
            await _communityService.LoadAsync();


            _allCommunity = _communityService.Entries.ToList();
            Dispatcher.Invoke(() =>
            {
                CommunityPanel.Items.Clear();
                foreach (var entry in _allCommunity)
                    CommunityPanel.Items.Add(CreateCommunityCard(entry));


                CommunityCountText.Text = $"{_allCommunity.Count} entries";
                CommunityStatusText.Text = "Ready";
            });
        }


        private Border CreateCommunityCard(CommunityFflagEntry entry)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                Margin = new Thickness(2),
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


            if (!string.IsNullOrEmpty(entry.PreviewImage))
            {
                try
                {
                    BitmapImage bitmap;
                    if (entry.PreviewImage.StartsWith("data:"))
                    {
                        var base64Data = entry.PreviewImage.Split(',')[1];
                        var bytes = Convert.FromBase64String(base64Data);
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                        bitmap.DecodePixelWidth = 300;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                    else
                    {
                        bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(entry.PreviewImage);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 300;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }


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
                    imageBorder.Child = CreatePlaceholderIcon(entry.Name);
                }
            }
            else
            {
                imageBorder.Child = CreatePlaceholderIcon(entry.Name);
            }


            outerStack.Children.Add(imageBorder);


            var infoStack = new StackPanel
            {
                Margin = new Thickness(8, 4, 8, 6)
            };


            infoStack.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 11,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            });


            if (!string.IsNullOrEmpty(entry.Description))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = entry.Description,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }


            if (entry.Tags.Count > 0)
            {
                var tagsStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var tag in entry.Tags.Take(3))
                {
                    var tagBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 4, 0)
                    };
                    tagBorder.Child = new TextBlock
                    {
                        Text = tag,
                        FontSize = 8,
                        Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85))
                    };
                    tagsStack.Children.Add(tagBorder);
                }
                infoStack.Children.Add(tagsStack);
            }


            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            metaStack.Children.Add(new TextBlock
            {
                Text = $"by {entry.AuthorName}",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (entry.Downloads > 0)
            {
                metaStack.Children.Add(new TextBlock
                {
                    Text = $"  \u00B7  {entry.Downloads} downloads",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            infoStack.Children.Add(metaStack);


            var btnGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });


            var previewBtn = CreateButton("Preview", "#0A0A0A", "#1A1A1A", new SolidColorBrush(Color.FromRgb(68, 68, 68)), "Eye20");
            previewBtn.Click += (s, e) => ShowPreview(entry);
            previewBtn.Margin = new Thickness(0);
            previewBtn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(previewBtn, 0);


            var addBtn = CreateButton("Add", "#0B2E1A", "#1A1A1A", new SolidColorBrush(Color.FromRgb(34, 197, 94)), "Add20");
            addBtn.Click += (s, e) => AddFlags(entry);
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


        private Button CreateButton(string text, string bg, string border, Brush foreground, string iconSymbol)
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


            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };


            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = ParseSymbol(iconSymbol),
                FontSize = 11,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };


            var label = new TextBlock
            {
                Text = text,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };


            stack.Children.Add(icon);
            stack.Children.Add(label);
            btn.Content = stack;


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


        private Wpf.Ui.Controls.SymbolRegular ParseSymbol(string name) => name switch
        {
            "Eye20" => Wpf.Ui.Controls.SymbolRegular.Eye20,
            "Add20" => Wpf.Ui.Controls.SymbolRegular.Add20,
            _ => Wpf.Ui.Controls.SymbolRegular.Eye20
        };


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


        private void ShowPreview(CommunityFflagEntry entry)
        {
            PreviewEntryName.Text = entry.Name;
            PreviewFlagCount.Text = $"{entry.FFlagCount} flags";


            var preview = entry.FFlags.Take(30).ToDictionary(k => k.Key, v =>
            {
                var val = v.Value;
                return val.ValueKind == JsonValueKind.String
                    ? (object)(val.GetString() ?? "")
                    : val.GetRawText();
            });


            PreviewJsonBox.Text = JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true });
            if (entry.FFlagCount > 30)
                PreviewJsonBox.Text += $"\n\n... and {entry.FFlagCount - 30} more flags";


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


        private void AddFlags(CommunityFflagEntry entry)
        {
            _flagService.CurrentFlags.Clear();


            foreach (var kvp in entry.FFlags)
            {
                var val = kvp.Value;
                string valueStr = val.ValueKind == JsonValueKind.String
                    ? val.GetString() ?? ""
                    : val.GetRawText();
                _flagService.AddFlag(kvp.Key, valueStr, "String");
            }


            _console.Log($"Added {entry.FFlagCount} flags from community \"{entry.Name}\"", "Success");
            CommunityStatusText.Text = $"Added {entry.FFlagCount} flags from {entry.Name}";
        }


        private void ClosePreview_Click(object sender, MouseButtonEventArgs e)
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
        }


        private void ClosePreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            PreviewOverlay.Visibility = Visibility.Collapsed;
        }


        private void PreviewAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PreviewEntryName.Text))
            {
                var entry = _allCommunity.FirstOrDefault(en => en.Name == PreviewEntryName.Text);
                if (entry != null)
                {
                    AddFlags(entry);
                    PreviewOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }


        private void CommunitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = CommunitySearchBox.Text.Trim().ToLower();
            CommunityPanel.Items.Clear();


            var filtered = string.IsNullOrEmpty(query)
                ? _allCommunity
                : _allCommunity.Where(en =>
                    en.Name.ToLower().Contains(query) ||
                    en.Description.ToLower().Contains(query) ||
                    en.AuthorName.ToLower().Contains(query) ||
                    en.Tags.Any(t => t.ToLower().Contains(query))
                ).ToList();


            foreach (var entry in filtered)
                CommunityPanel.Items.Add(CreateCommunityCard(entry));


            CommunityCountText.Text = $"{filtered.Count} entries";
        }


        private void OnLoaded(object sender, RoutedEventArgs e)
        {
        }
    }
}
