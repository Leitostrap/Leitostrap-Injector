using System;
using System.Collections.Specialized;
using System.Threading;
using DiscordRPC;
using DiscordRPC.Logging;


namespace LeitostrapV7.Core;


public sealed class DiscordService : IDisposable
{
    public static DiscordService Instance { get; } = new();
    private DiscordRpcClient _client;
    private DateTime _startTime;
    private string _currentSection = "Home";
    private int _flagCount;
    private bool _enabled;
    private bool _disposed;


    private const string ApplicationId = "1477025781522759721";
    private const string DiscordUrl = "https://discord.gg/Ga2nGGPgJk";
    private const string WebsiteUrl = "https://leitostrap.netlify.app/";


    private static readonly string[] SectionNames =
    {
        "Home",
        "FFlags Editor",
        "Offsets DataBase",
        "Games FFlags",
        "Console",
        "Roblox Clients",
        "Roblox Versions",
        "Themes",
        "Settings",
        "About"
    };


    private DiscordService() { }


    public void Init()
    {
        if (_enabled) return;
        try
        {
            _enabled = SettingsService.Instance.Get<bool>("rpc_enabled");
        }
        catch
        {
            _enabled = true;
        }
        if (!_enabled) return;


        StartClient();


        FFlagService.Instance.CurrentFlags.CollectionChanged += OnFlagsChanged;
    }


    private void OnFlagsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        _flagCount = FFlagService.Instance.CurrentFlags.Count;
        PushPresence();
    }


    private void StartClient()
    {
        try
        {
            _client?.Dispose();
            _client = new DiscordRpcClient(ApplicationId);
            _client.Logger = new ConsoleLogger(LogLevel.None, false);
            _client.Initialize();
            _startTime = DateTime.UtcNow;
            PushPresence();
            ConsoleService.Instance.Log("Discord RPC connected", "Success");
        }
        catch (Exception ex)
        {
            ConsoleService.Instance.Log($"Discord RPC failed: {ex.Message}", "Warning");
            _client = null;
        }
    }


    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        SettingsService.Instance.Set("rpc_enabled", enabled);
        if (enabled)
            StartClient();
        else
            Shutdown();
    }


    public bool IsEnabled => _enabled;


    public void SetSection(int index, int flagCount = -1)
    {
        if (!_enabled || _client == null) return;
        if (index >= 0 && index < SectionNames.Length)
            _currentSection = SectionNames[index];
        if (flagCount >= 0)
            _flagCount = flagCount;
        PushPresence();
    }


    public void UpdateFlagCount(int count)
    {
        if (!_enabled || _client == null) return;
        _flagCount = count;
        PushPresence();
    }


    private void PushPresence()
    {
        try
        {
            if (_client == null) return;


            string section = _currentSection;
            foreach (var prefix in new[] { "\U0001F3E0 ", "\U0001F4CC ", "\U0001F4E6 ", "\U0001F3AE ", "\U0001F4BB ", "\U0001F5A5 ", "\U0001F4E5 ", "\U0001F3A8 ", "\u2699\uFE0F ", "\u2139\uFE0F " })
            {
                if (section.StartsWith(prefix))
                {
                    section = section[prefix.Length..];
                    break;
                }
            }


            string flagLine = _flagCount > 0
                ? $", FFlags Imported: {_flagCount:N0}"
                : "";


            _client.SetPresence(new RichPresence
            {
                Details = "\U0001F680 Best FastFlag Injector for Roblox",
                State = $"Section: {section}{flagLine}",
                Timestamps = new Timestamps(_startTime),
                Buttons = new[]
                {
                    new Button
                    {
                        Label = "\U0001F4E5 Download",
                        Url = WebsiteUrl
                    },
                    new Button
                    {
                        Label = "\U0001F4AC Discord",
                        Url = DiscordUrl
                    }
                },
                Assets = new Assets
                {
                    LargeImageKey = "leitostrap",
                    LargeImageText = "Leitostrap V7"
                }
            });
        }
        catch { }
    }


    public void Shutdown()
    {
        try
        {
            _client?.Dispose();
            _client = null;
        }
        catch { }
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }
}
