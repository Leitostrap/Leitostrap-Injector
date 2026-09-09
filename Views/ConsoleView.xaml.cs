using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LeitostrapV7.Core;


namespace LeitostrapV7.Views
{
    public partial class ConsoleView : UserControl
    {
        private readonly ConsoleService _console = ConsoleService.Instance;
        private readonly string _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leitostrap Injector", "Logs");
        private string _currentLogPath;


        public ConsoleView()
        {
            InitializeComponent();
            ConsoleService.OnLogAdded += OnLogAdded;
            Directory.CreateDirectory(_logDir);
            _currentLogPath = Path.Combine(_logDir, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            AppendLine("Leitostrap System Console Initialized...", "#4CAF50");
            AppendLine($"Log file: {_currentLogPath}", "#555555");
            AppendLine("", "#555555");
        }


        private void OnLogAdded(LogEntry entry)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string color = entry.Level?.ToUpper() switch
                {
                    "SUCCESS" => "#4CAF50",
                    "WARNING" => "#FF9800",
                    "ERROR" => "#F44336",
                    _ => "#9E9E9E"
                };
                string line = $"[{entry.Time:HH:mm:ss}] [{entry.Level}] {entry.Message}";
                AppendLine(line, color);
            }));
        }


        private void AppendLine(string text, string color = "#AAAAAA")
        {
            if (ConsoleOutput.Text.Length > 0) ConsoleOutput.Text += Environment.NewLine;
            ConsoleOutput.Text += text;
            try { File.AppendAllText(_currentLogPath, text + Environment.NewLine); } catch { }
            ConsoleScroll.ScrollToEnd();
        }


        private void CopyLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ConsoleOutput.Text); _console.Log("Log copied to clipboard", "Success"); NotificationService.Instance.Show("Log copied to clipboard", NotificationType.Success, 2000); } catch { }
        }


        private void OpenLogBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_currentLogPath)) Process.Start(new ProcessStartInfo(_currentLogPath) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("explorer.exe", _logDir) { UseShellExecute = true });
            }
            catch (Exception ex) { _console.Log($"Failed to open log: {ex.Message}", "Error"); }
        }


        private void ClearBtn_Click(object sender, RoutedEventArgs e) { ConsoleOutput.Text = ""; _console.Clear(); AppendLine("Console cleared", "#555555"); NotificationService.Instance.Show("Console cleared", NotificationType.Info, 2000); }


        public void RefreshLog()
        {


            ConsoleOutput.Text = "";
            foreach (var entry in _console.Entries)
            {
                string color = entry.Level?.ToUpper() switch
                {
                    "SUCCESS" => "#4CAF50",
                    "WARNING" => "#FF9800",
                    "ERROR" => "#F44336",
                    _ => "#9E9E9E"
                };
                string line = $"[{entry.Time:HH:mm:ss}] [{entry.Level}] {entry.Message}";
                if (ConsoleOutput.Text.Length > 0) ConsoleOutput.Text += Environment.NewLine;
                ConsoleOutput.Text += line;
            }
            ConsoleScroll.ScrollToEnd();
        }


        protected override void OnVisualParentChanged(DependencyObject oldParent) { base.OnVisualParentChanged(oldParent); ConsoleScroll.ScrollToEnd(); }
    }
}
