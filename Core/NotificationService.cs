using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Wpf.Ui.Controls;


namespace LeitostrapV7.Core;


public enum NotificationType { Success, Error, Warning, Info }


public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();


    private StackPanel? _toastHost;
    private string _currentAccent = "#FFFFFF";


    private NotificationService() { }


    public void SetHost(StackPanel host) => _toastHost = host;


    public void SetAccent(string hex) => _currentAccent = hex;


    public void Show(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
    {
        if (_toastHost == null) return;
        Application.Current.Dispatcher.Invoke(() => ShowToast(message, type, durationMs));
    }


    private void ShowToast(string message, NotificationType type, int durationMs)
    {
        var (accentColor, icon) = type switch
        {
            NotificationType.Success => ("#FFFFFF", SymbolRegular.CheckmarkCircle20),
            NotificationType.Error => ("#555555", SymbolRegular.DismissCircle20),
            NotificationType.Warning => ("#888888", SymbolRegular.Warning20),
            _ => (_currentAccent, SymbolRegular.Info20),
        };


        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentColor));
        var bgRgb = (Color)ColorConverter.ConvertFromString(accentColor);
        var toastBg = new SolidColorBrush(Color.FromArgb(18, bgRgb.R, bgRgb.G, bgRgb.B));


        var bellIcon = new SymbolIcon
        {
            Symbol = SymbolRegular.Alert20,
            FontSize = 16,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };


        var bellRotate = new RotateTransform(0);
        bellIcon.RenderTransform = bellRotate;
        bellIcon.RenderTransformOrigin = new Point(0.5, 0.5);


        var ringAnim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(600),
            FillBehavior = FillBehavior.Stop
        };
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(15, KeyTime.FromPercent(0.15)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-12, KeyTime.FromPercent(0.3)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromPercent(0.45)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-5, KeyTime.FromPercent(0.6)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromPercent(0.75)));
        ringAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        bellRotate.BeginAnimation(RotateTransform.AngleProperty, ringAnim);


        var statusIcon = new SymbolIcon
        {
            Symbol = icon,
            FontSize = 14,
            Foreground = accentBrush,
            VerticalAlignment = VerticalAlignment.Center
        };


        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = message,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
            VerticalAlignment = VerticalAlignment.Center
        };


        var contentStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { bellIcon, statusIcon, textBlock }
        };


        var toast = new Border
        {
            Background = toastBg,
            BorderBrush = accentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = contentStack
        };


        toast.RenderTransform = new TranslateTransform(300, 0);
        _toastHost!.Children.Add(toast);


        var slideIn = new DoubleAnimation(300, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);


        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);


        setTimeout(() => RemoveToast(toast), durationMs);
    }


    private void RemoveToast(Border toast)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var slideOut = new DoubleAnimation(0, 300, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) => _toastHost?.Children.Remove(toast);
            toast.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
            toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        });
    }


    private void setTimeout(Action action, int delayMs)
    {
        var timer = new System.Timers.Timer(delayMs) { AutoReset = false };
        timer.Elapsed += (_, _) => { timer.Dispose(); action(); };
        timer.Start();
    }
}
