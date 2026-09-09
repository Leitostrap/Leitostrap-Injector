using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;


namespace LeitostrapV7.Core;


public class GameInfo
{
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string JsonPath { get; set; } = "";
    public Dictionary<string, JsonElement> FFlags { get; set; } = new();
    public int FFlagCount => FFlags.Count;
    public string Description { get; set; } = "";


    public string GetDescription()
    {
        if (Name == "All Games") return "Universal FFlags that work across all Roblox games. FPS boost, ping reduction, telemetry removal, and network optimization.";
        if (FFlags.Count > 100) return $"Comprehensive FFlag set with {FFlags.Count} flags for maximum performance optimization.";
        if (FFlags.Count > 50) return $"Optimized FFlag collection with {FFlags.Count} flags for improved gameplay experience.";
        return $"Focused FFlag preset with {FFlags.Count} flags tailored for {Name}.";
    }
}


public class GameFflagsService
{
    private static GameFflagsService? _instance;
    public static GameFflagsService Instance => _instance ??= new GameFflagsService();


    public List<GameInfo> Games { get; private set; } = new();


    private GameFflagsService()
    {
        LoadGames();
    }


    private void LoadGames()
    {
        Games.Clear();
        var basePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Games FFlags");


        if (!Directory.Exists(basePath))
            return;


        var folders = Directory.GetDirectories(basePath);


        foreach (var folder in folders)
        {
            var folderName = Path.GetFileName(folder);
            var pngFiles = Directory.GetFiles(folder, "*.png");
            var jsonFiles = Directory.GetFiles(folder, "*.json");


            if (jsonFiles.Length == 0) continue;


            var primaryJson = FindPrimaryJson(jsonFiles);
            if (primaryJson == null) continue;


            var game = new GameInfo
            {
                Name = folderName,
                FolderName = folderName,
                ImagePath = pngFiles.Length > 0 ? pngFiles[0] : "",
                JsonPath = primaryJson
            };


            try
            {
                var jsonContent = File.ReadAllText(primaryJson);
                game.FFlags = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent) ?? new();
                game.Description = game.GetDescription();
            }
            catch { continue; }


            Games.Add(game);
        }


        Games.Sort((a, b) =>
        {
            if (a.Name == "All Games") return -1;
            if (b.Name == "All Games") return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }


    private string? FindPrimaryJson(string[] jsonFiles)
    {
        foreach (var f in jsonFiles)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!name.Contains("_v") && !name.Contains("Optimization"))
                return f;
        }
        return jsonFiles[0];
    }
}
