using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;


namespace LeitostrapV7.Core;


public class Profile
{
    public string Name { get; set; } = "";
    public string Game { get; set; } = "";
    public List<FFlagEntry> Flags { get; set; } = new();
}


public class ProfileService
{
    private static ProfileService? _instance;
    public static ProfileService Instance => _instance ??= new ProfileService();


    private readonly Dictionary<string, List<Profile>> _categories;


    private ProfileService()
    {
        _categories = new Dictionary<string, List<Profile>>();
        LoadDefaultProfiles();
    }


    private void LoadDefaultProfiles()
    {


        _categories["Universal"] = new List<Profile>
        {
            new Profile
            {
                Name = "Performance Mode",
                Game = "Universal",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "0", Type = "DFInt" },
                    new() { Name = "FFlagDebugForceFSMCPULoop", Value = "True", Type = "FFlag" },
                    new() { Name = "DFIntDebugRestrictGCDivisor", Value = "0", Type = "DFInt" },
                    new() { Name = "FFlagGameBasicSettingsFramerateCap", Value = "9999", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Anti-AFK",
                Game = "Universal",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "FFlagClientVirtualEventReplicator1", Value = "True", Type = "FFlag" },
                    new() { Name = "DFIntClientPresenceUpdateFrequency", Value = "1", Type = "DFInt" },
                    new() { Name = "FFlagDebugSimulateNoMemory", Value = "False", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Boost FPS",
                Game = "Universal",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerThreadMinSize", Value = "4", Type = "DFInt" },
                    new() { Name = "DFIntTaskSchedulerThreadMaxSize", Value = "8", Type = "DFInt" },
                    new() { Name = "DFIntRenderLocalLightUpdatesMax", Value = "12", Type = "DFInt" },
                    new() { Name = "DFIntRenderLocalLightUpdatesMin", Value = "4", Type = "DFInt" },
                    new() { Name = "FFlagDebugGraphicsPreferVulkan", Value = "True", Type = "FFlag" },
                }
            },
        };


        _categories["Blox Fruits"] = new List<Profile>
        {
            new Profile
            {
                Name = "Fruits ESP",
                Game = "Blox Fruits",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntDebugFRMQualityLevelOverride", Value = "1", Type = "DFInt" },
                    new() { Name = "FFlagRenderDebugLove", Value = "False", Type = "FFlag" },
                    new() { Name = "FFlagDebugAntiSpamTemp", Value = "False", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Auto Farm",
                Game = "Blox Fruits",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "9999", Type = "DFInt" },
                    new() { Name = "FFlagGameBasicSettingsFramerateCap", Value = "9999", Type = "FFlag" },
                    new() { Name = "DFIntClientPresenceUpdateFrequency", Value = "1", Type = "DFInt" },
                }
            },
            new Profile
            {
                Name = "PvP Mode",
                Game = "Blox Fruits",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "0", Type = "DFInt" },
                    new() { Name = "FFlagDebugForceFSMCPULoop", Value = "True", Type = "FFlag" },
                    new() { Name = "DFIntDebugRestrictGCDivisor", Value = "0", Type = "DFInt" },
                }
            },
        };


        _categories["King Legacy"] = new List<Profile>
        {
            new Profile
            {
                Name = "Fruits ESP",
                Game = "King Legacy",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntDebugFRMQualityLevelOverride", Value = "1", Type = "DFInt" },
                    new() { Name = "FFlagRenderDebugLove", Value = "False", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Auto Farm",
                Game = "King Legacy",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "9999", Type = "DFInt" },
                    new() { Name = "FFlagGameBasicSettingsFramerateCap", Value = "9999", Type = "FFlag" },
                }
            },
        };


        _categories["Muscle Legends"] = new List<Profile>
        {
            new Profile
            {
                Name = "Auto Farm",
                Game = "Muscle Legends",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "9999", Type = "DFInt" },
                    new() { Name = "FFlagGameBasicSettingsFramerateCap", Value = "9999", Type = "FFlag" },
                    new() { Name = "DFIntClientPresenceUpdateFrequency", Value = "1", Type = "DFInt" },
                }
            },
            new Profile
            {
                Name = "Rebirth Auto",
                Game = "Muscle Legends",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "FFlagDebugSimulateNoMemory", Value = "False", Type = "FFlag" },
                    new() { Name = "DFIntDebugRestrictGCDivisor", Value = "0", Type = "DFInt" },
                }
            },
        };


        _categories["Pet Simulator X"] = new List<Profile>
        {
            new Profile
            {
                Name = "Pet ESP",
                Game = "Pet Simulator X",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntDebugFRMQualityLevelOverride", Value = "1", Type = "DFInt" },
                    new() { Name = "FFlagRenderDebugLove", Value = "False", Type = "FFlag" },
                    new() { Name = "FFlagDebugAntiSpamTemp", Value = "False", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Auto Farm",
                Game = "Pet Simulator X",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "9999", Type = "DFInt" },
                    new() { Name = "FFlagGameBasicSettingsFramerateCap", Value = "9999", Type = "FFlag" },
                }
            },
            new Profile
            {
                Name = "Hatch Eggs",
                Game = "Pet Simulator X",
                Flags = new List<FFlagEntry>
                {
                    new() { Name = "DFIntTaskSchedulerTargetFps", Value = "0", Type = "DFInt" },
                    new() { Name = "DFIntClientPresenceUpdateFrequency", Value = "1", Type = "DFInt" },
                    new() { Name = "FFlagDebugSimulateNoMemory", Value = "False", Type = "FFlag" },
                }
            },
        };
    }


    public Dictionary<string, List<Profile>> GetDefaultProfiles()
    {
        return _categories;
    }


    public List<Profile>? GetByCategory(string category)
    {
        _categories.TryGetValue(category, out var profiles);
        return profiles;
    }


    public List<string> GetCategories()
    {
        return new List<string>(_categories.Keys);
    }


    public void ApplyProfile(Profile profile)
    {
        var fflagService = FFlagService.Instance;
        foreach (var flag in profile.Flags)
        {
            fflagService.AddFlag(flag.Name, flag.Value, flag.Type);
        }
    }
}
