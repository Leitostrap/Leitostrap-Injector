using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;


namespace LeitostrapV7.Core;


public class LogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Message { get; set; } = "";
    public string Level { get; set; } = "Info";
    public Brush Color => Level switch
    {
        "Success" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
        "Warning" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
        "Error" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA))
    };
}


public class ConsoleService
{
    private static ConsoleService? _instance;
    public static ConsoleService Instance => _instance ??= new ConsoleService();
    public ObservableCollection<LogEntry> Entries { get; } = new();
    public static event Action<LogEntry>? OnLogAdded;


    public void Log(string message, string level = "Info")
    {
        var entry = new LogEntry { Message = message, Level = level };
        Application.Current.Dispatcher.Invoke(() =>
        {
            Entries.Add(entry);
            OnLogAdded?.Invoke(entry);
        });
    }


    public void Clear() => Application.Current.Dispatcher.Invoke(() => Entries.Clear());
}
