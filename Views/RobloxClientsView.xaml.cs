using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LeitostrapV7.Core;


namespace LeitostrapV7.Views
{
    public partial class RobloxClientsView : UserControl
    {
        private readonly ProcessService _processService = ProcessService.Instance;
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly System.Timers.Timer _refreshTimer;
        private List<RobloxProcessInfo> _currentClients = new();


        public RobloxClientsView()
        {
            InitializeComponent();
            _refreshTimer = new System.Timers.Timer(3000);
            _refreshTimer.Elapsed += (s, e) =>
            {
                try { Dispatcher.BeginInvoke(new Action(RefreshClients)); } catch { }
            };
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
            Loaded += OnLoaded;
        }


        private void OnLoaded(object sender, RoutedEventArgs e) => RefreshClients();


        private async void RefreshClients()
        {
            try
            {
                _currentClients = await System.Threading.Tasks.Task.Run(() => _processService.FindClients());
                CountText.Text = _currentClients.Count.ToString();
                RenderClients();
            }
            catch { }
        }


        private void RenderClients()
        {
            ClientsListPanel.Children.Clear();
            EmptyStatePanel.Visibility = _currentClients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;


            foreach (var client in _currentClients)
                ClientsListPanel.Children.Add(CreateClientCard(client));
        }


        private Border CreateClientCard(RobloxProcessInfo client)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };


            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });


            var logoBorder = new Border
            {
                Width = 42, Height = 42,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            try
            {
                var logoImg = new System.Windows.Controls.Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("/LeitostrapV7;component/Resources/Roblox.png", UriKind.Relative)),
                    Width = 42, Height = 42,
                    Stretch = Stretch.UniformToFill
                };
                logoBorder.Child = logoImg;
            }
            catch { }
            Grid.SetColumn(logoBorder, 0);
            grid.Children.Add(logoBorder);


            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(infoStack, 2);


            var gameName = string.IsNullOrEmpty(client.GameName) ? "Roblox" : client.GameName;
            infoStack.Children.Add(new TextBlock
            {
                Text = gameName,
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });


            var detailStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            detailStack.Children.Add(new TextBlock
            {
                Text = $"PID: {client.PID}",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))
            });
            detailStack.Children.Add(new TextBlock
            {
                Text = $"  |  {client.MemoryMB} MB",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
            });
            if (!string.IsNullOrEmpty(client.Username))
            {
                detailStack.Children.Add(new TextBlock
                {
                    Text = $"  |  {client.Username}",
                    FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                });
            }
            infoStack.Children.Add(detailStack);


            if (!string.IsNullOrEmpty(client.GameName) && client.GameName != "Unknown Game" && client.GameName != "Roblox")
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = client.GameName,
                    FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }


            grid.Children.Add(infoStack);


            var killBtn = new Button
            {
                Style = (Style)FindResource("CardBtn"),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = client.PID
            };
            killBtn.Click += KillClient_Click;
            var killStack = new StackPanel { Orientation = Orientation.Horizontal };
            killStack.Children.Add(new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss20,
                FontSize = 11,
                Margin = new Thickness(0, 0, 5, 0)
            });
            killStack.Children.Add(new TextBlock { Text = "Kill", FontSize = 10 });
            killBtn.Content = killStack;
            Grid.SetColumn(killBtn, 4);
            grid.Children.Add(killBtn);


            card.Child = grid;
            return card;
        }


        private void KillClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int pid)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    proc.Kill();
                    _console.Log($"Killed client PID {pid}", "Warning");
                    NotificationService.Instance.Show($"Killed client PID {pid}", NotificationType.Warning, 2000);
                    RefreshClients();
                }
                catch (Exception ex)
                {
                    _console.Log($"Failed to kill PID {pid}: {ex.Message}", "Error");
                }
            }
        }


        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshBtn.IsEnabled = false;
            var rotate = new RotateTransform(0);
            RefreshIcon.RenderTransform = rotate;
            RefreshIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            spin.Completed += (_, _) => { RefreshBtn.IsEnabled = true; };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            RefreshClients();
        }
    }
}
