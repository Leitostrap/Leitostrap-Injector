using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;


namespace LeitostrapV7.Core;


public class SettingsService
{
    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();


    private readonly string _settingsPath;
    private Dictionary<string, JsonElement> _settings = new();


    private SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leitostrap Injector");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        Load();
    }


    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
        }
        catch { _settings = new(); }
    }


    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }


    public T Get<T>(string key, T defaultValue = default!)
    {
        if (_settings.TryGetValue(key, out var el))
        {
            try
            {
                if (typeof(T) == typeof(bool)) return (T)(object)el.GetBoolean();
                if (typeof(T) == typeof(int)) return (T)(object)el.GetInt32();
                if (typeof(T) == typeof(string)) return (T)(object)(el.GetString() ?? "");
                return JsonSerializer.Deserialize<T>(el.GetRawText()) ?? defaultValue;
            }
            catch { }
        }
        return defaultValue;
    }


    public void Set(string key, object value)
    {
        _settings[key] = JsonSerializer.SerializeToElement(value);
        Save();
    }
}
