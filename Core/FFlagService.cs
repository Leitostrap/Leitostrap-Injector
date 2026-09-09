using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;


namespace LeitostrapV7.Core;


public class FFlagEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Type { get; set; } = "FFlag";
    public bool IsSelected { get; set; }
}


public static class FFlagTypeSource
{
    public static System.Collections.ObjectModel.ObservableCollection<string> Types { get; } =
        new(new[] { "FFlag", "DFFlag", "FInt", "DFInt", "FBool", "DFBool", "FFlagString", "Flag", "String", "Int", "Bool" });
}


public class FFlagService
{
    private static FFlagService? _instance;
    public static FFlagService Instance => _instance ??= new FFlagService();


    public ObservableCollection<FFlagEntry> CurrentFlags { get; } = new();
    public Dictionary<string, ObservableCollection<FFlagEntry>> Tabs { get; } = new();


    public FFlagService()
    {
        Tabs["Default"] = CurrentFlags;
    }


    public void AddFlag(string name, string value, string type = "FFlag")
    {
        CurrentFlags.Add(new FFlagEntry
        {
            Name = name,
            Value = value,
            Type = type,
            IsSelected = false
        });
    }


    public void RemoveFlag(FFlagEntry flag)
    {
        CurrentFlags.Remove(flag);
    }


    public void RemoveSelected()
    {
        var selected = new List<FFlagEntry>();
        foreach (var flag in CurrentFlags)
        {
            if (flag.IsSelected)
                selected.Add(flag);
        }


        foreach (var flag in selected)
            CurrentFlags.Remove(flag);
    }


    public void ImportFromJson(string path)
    {
        if (!File.Exists(path))
            return;


        try
        {
            string json = File.ReadAllText(path);
            json = json.Trim();
            using var doc = JsonDocument.Parse(json);


            CurrentFlags.Clear();


            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var entries = JsonSerializer.Deserialize<List<FFlagEntry>>(json, options);
                if (entries != null)
                    foreach (var entry in entries)
                        CurrentFlags.Add(entry);
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string name = prop.Name;
                    string value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                    if (!string.IsNullOrEmpty(name))
                        CurrentFlags.Add(new FFlagEntry { Name = name, Value = value, Type = "FFlag" });
                }
            }
        }
        catch (Exception)
        {
        }
    }


    public void ExportToJson(string path)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(new List<FFlagEntry>(CurrentFlags), options);
            File.WriteAllText(path, json);
        }
        catch (Exception)
        {


        }
    }


    public string GetJsonString()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(new List<FFlagEntry>(CurrentFlags), options);
        }
        catch (Exception)
        {
            return "[]";
        }
    }


    public void AddTab(string name)
    {
        if (!Tabs.ContainsKey(name))
            Tabs[name] = new ObservableCollection<FFlagEntry>();
    }


    public void RemoveTab(string name)
    {
        if (name != "Default")
            Tabs.Remove(name);
    }


    public ObservableCollection<FFlagEntry>? GetTab(string name)
    {
        Tabs.TryGetValue(name, out var tab);
        return tab;
    }


    public static string TabsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leitostrap Injector", "tabs.json");


    public void SaveTabs(IReadOnlyList<string> tabOrder)
    {
        try
        {
            var data = new List<Dictionary<string, object>>();
            foreach (var name in tabOrder)
            {
                if (name == "Default") continue;
                if (!Tabs.TryGetValue(name, out var flags)) continue;
                data.Add(new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["flags"] = new List<FFlagEntry>(flags)
                });
            }
            string dir = Path.GetDirectoryName(TabsFilePath) ?? "";
            Directory.CreateDirectory(dir);
            File.WriteAllText(TabsFilePath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }


    public List<string> LoadTabs()
    {
        var restoredOrder = new List<string>();
        try
        {
            if (!File.Exists(TabsFilePath)) return restoredOrder;
            var data = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(File.ReadAllText(TabsFilePath));
            if (data == null) return restoredOrder;
            foreach (var item in data)
            {
                string name = item.TryGetValue("name", out var n) ? (n.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(name) || name == "Default") continue;
                var flags = new ObservableCollection<FFlagEntry>();
                if (item.TryGetValue("flags", out var flagsEl) && flagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in flagsEl.EnumerateArray())
                    {
                        if (f.ValueKind != JsonValueKind.Object) continue;
                        string fname = f.TryGetProperty("Name", out var pn) ? (pn.GetString() ?? "")
                            : f.TryGetProperty("name", out pn) ? (pn.GetString() ?? "") : "";
                        string fval = f.TryGetProperty("Value", out var pv) ? (pv.GetString() ?? "")
                            : f.TryGetProperty("value", out pv) ? (pv.GetString() ?? "") : "";
                        string ftype = f.TryGetProperty("Type", out var pt) ? (pt.GetString() ?? "FFlag")
                            : f.TryGetProperty("type", out pt) ? (pt.GetString() ?? "FFlag") : "FFlag";
                        if (!string.IsNullOrEmpty(fname))
                            flags.Add(new FFlagEntry { Name = fname, Value = fval, Type = ftype });
                    }
                }
                Tabs[name] = flags;
                restoredOrder.Add(name);
            }
        }
        catch { }
        return restoredOrder;
    }


    public void DeleteTabsFile()
    {
        try { if (File.Exists(TabsFilePath)) File.Delete(TabsFilePath); } catch { }
    }
}
