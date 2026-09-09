using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


namespace LeitostrapV7.Core;


public class OffsetItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "0x0";
    public string Type { get; set; } = "Cache";
}


public class OffsetService
{
    private static OffsetService? _instance;
    public static OffsetService Instance => _instance ??= new OffsetService();


    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Leitostrap Injector");


    private static readonly string OffsetsCacheDir = Path.Combine(AppDataPath, "offsets_cache");


    private static readonly Regex OffsetRegex = new(@"(\w+)\s*=\s*""([^""]+)""|(\w+)\s*=\s*(0x[0-9A-Fa-f]+)", RegexOptions.Compiled);
    private static readonly Regex NamespaceRegex = new(@"namespace\s+(\w+)\s*\{", RegexOptions.Compiled);


    private Dictionary<string, OffsetItem> _allOffsets = new();
    private Dictionary<string, string> _memoryOffsets = new();
    private Dictionary<string, string> _cacheOffsets = new();
    private readonly object _cacheLock = new();
    private string _currentVersion = "";
    private bool _fetched;
    private bool _cacheFetched;
    private int _totalFromLink;


    private static readonly string[] CacheHosts = new[]
    {
        "clientsettingscdn.roblox.com",
        "clientsettings.roblox.com"
    };


    private static readonly string[] SettingsPaths = new[]
    {
        "/settings/application/",
        "/settings-compressed/application/",
        "/v2/settings/application/",
        "/v2/settings-compressed/application/"
    };


    public string CurrentVersion => _currentVersion;
    public bool IsFetched => _fetched;
    public int TotalFromLink => _totalFromLink;


    public void SetCurrentVersion(string version)
    {
        _currentVersion = version;
        _cacheOffsets["ClientVersion"] = version;
        _allOffsets["ClientVersion"] = new OffsetItem { Name = "ClientVersion", Value = version, Type = "Cache" };
    }


    public async Task WaitForCacheOffsets()
    {
        if (_cacheFetched) return;
        for (int i = 0; i < 60; i++)
        {
            if (_cacheFetched) return;
            await Task.Delay(500);
        }
    }


    public async Task ReloadCacheOffsetsAsync()
    {
        lock (_cacheFetchLock)
        {
            _cacheFetched = false;
            _cacheFetching = false;
            _cacheFetchTask = null;


            var cacheKeys = _cacheOffsets.Keys.ToList();
            foreach (var k in cacheKeys) _cacheOffsets.Remove(k);
            var allKeys = _allOffsets.Where(p => p.Value.Type == "Cache" || p.Value.Type == "CacheOffset")
                .Select(p => p.Key).ToList();
            foreach (var k in allKeys) _allOffsets.Remove(k);
        }
        await FetchCacheOffsetsAsync();
    }


    public Task EnsureCacheOffsetsReadyAsync()
    {
        if (_cacheFetched) return Task.CompletedTask;
        return FetchCacheOffsetsAsync();
    }


    private OffsetService()
    {
        Directory.CreateDirectory(OffsetsCacheDir);
    }


    private bool _fetching;


    public async Task FetchOffsetsAsync()
    {
        if (_fetching) return;
        _fetching = true;
        NotificationService.Instance.Show("Fetching offsets from imtheo...", NotificationType.Info, 2500);


        _currentVersion = GetRunningRobloxVersion();
        if (string.IsNullOrEmpty(_currentVersion))
        {
            NotificationService.Instance.Show("No Roblox version detected", NotificationType.Warning, 3000);
        }


        NotificationService.Instance.Show($"Detected version: {_currentVersion}", NotificationType.Info, 2500);


        try { await Task.WhenAny(Task.Run(() => TryFetchVersionSpecific(_currentVersion)), Task.Delay(8000)); }
        catch { }


        if (_fetched)
        {
            NotificationService.Instance.Show(
                $"Loaded {_memoryOffsets.Count:N0} memory offsets + {_cacheOffsets.Count:N0} cache offsets",
                NotificationType.Success, 4000);
        }
        else
        {
            NotificationService.Instance.Show("No offsets for this Roblox version. Update Roblox or wait for imtheo.", NotificationType.Warning, 5000);
        }


        await FetchCacheOffsetsAsync();
    }


    private object _cacheFetchLock = new();
    private bool _cacheFetching;
    private Task? _cacheFetchTask;


    private Task FetchCacheOffsetsAsync()
    {
        lock (_cacheFetchLock)
        {
            if (_cacheFetched) return Task.CompletedTask;
            if (_cacheFetching && _cacheFetchTask != null) return _cacheFetchTask;
            _cacheFetching = true;
            _cacheFetchTask = FetchCacheOffsetsAsyncCore();
            _ = _cacheFetchTask.ContinueWith(_ =>
            {
                lock (_cacheFetchLock) _cacheFetching = false;
            }, TaskScheduler.Default);
            return _cacheFetchTask;
        }
    }


    private async Task FetchCacheOffsetsAsyncCore()
    {
        if (_cacheFetched) return;


        var apps = new[]
        {
            "PCDesktopClient", "MacDesktopClient", "PlayStationClient", "XboxClient",
            "iOSApp", "UWPApp", "AndroidApp", "PCStudioApp", "MacStudioApp",
            "PCStudioBootstrapper", "MacStudioBootstrapper", "PCClientBootstrapper", "MacClientBootstrapper"
        };
        var buckets = new[] { "", "/bucket/zcanary", "/bucket/zintegration" };
        var baseUrl = "https://clientsettingscdn.roblox.com/v2/settings/application/";


        var tasks = new List<Task>();
        foreach (var app in apps)
        {
            foreach (var bucket in buckets)
            {
                string url = $"{baseUrl}{app}{bucket}";
                tasks.Add(FetchSingleSettings(url));
            }
        }


        tasks.Add(FetchTrackerFlags("https://raw.githubusercontent.com/MaximumADHD/Roblox-Client-Tracker/roblox/FVariables.txt"));
        tasks.Add(FetchTrackerFlags("https://raw.githubusercontent.com/MaximumADHD/Roblox-Client-Tracker/03a46e5f35e7aa5d85310189b477caee20b20761/FVariables.txt"));


        await Task.WhenAll(tasks);


        if (_cacheOffsets.Count > 0)
        {
            NotificationService.Instance.Show(
                $"Loaded {_cacheOffsets.Count:N0} cache offsets from Roblox settings + tracker",
                NotificationType.Success, 3000);
            ConsoleService.Instance.Log($"Offset base: {_cacheOffsets.Count:N0} unique flags (CDN + tracker union)", "Info");
        }
        else
        {
            NotificationService.Instance.Show("No cache offsets loaded from Roblox settings", NotificationType.Warning, 2000);
        }


        _cacheFetched = true;
    }


    private async Task FetchSingleSettings(string url)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
                    AutomaticDecompression = System.Net.DecompressionMethods.All
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.Add("User-Agent", "Leitostrap/7.0");
                string json = await client.GetStringAsync(url);
                ParseClientSettings(json);
                return;
            }
            catch { }
            await Task.Delay(800 * attempt);
        }
    }


    private async Task FetchTrackerFlags(string url)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.Add("User-Agent", "Leitostrap/7.0");
                byte[] data = await client.GetByteArrayAsync(url);
                string text = Encoding.UTF8.GetString(data);
                int parsed = ParseTrackerFlags(text);
                if (parsed > 0)
                {
                    ConsoleService.Instance.Log($"Loaded {parsed:N0} flag names from Roblox-Client-Tracker", "Info");
                    return;
                }
            }
            catch { }
            await Task.Delay(1000 * attempt);
        }
        ConsoleService.Instance.Log("Roblox-Client-Tracker unreachable, CDN flags only (about 26k of 38k)", "Warning");
    }


    private int ParseClientSettings(string json)
    {
        int count = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("applicationSettings", out JsonElement settings))
                return 0;


            lock (_cacheLock)
            {
                foreach (var prop in settings.EnumerateObject())
                {
                    string name = prop.Name;
                    string value = prop.Value.ValueKind switch
                    {
                        JsonValueKind.True => "True",
                        JsonValueKind.False => "False",
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.ToString(),
                        _ => ""
                    };


                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;


                    _cacheOffsets[name] = value;
                    _allOffsets[name] = new OffsetItem { Name = name, Value = value, Type = "Cache" };
                    count++;
                }
            }
        }
        catch { }
        return count;
    }


    private int ParseTrackerFlags(string text)
    {
        int added = 0;
        lock (_cacheLock)
        {
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int closeIdx = line.IndexOf(']');
                if (closeIdx < 0) continue;
                string name = line.Substring(closeIdx + 1).Trim();
                if (string.IsNullOrEmpty(name) || name.Contains(' ')) continue;


                if (!_cacheOffsets.ContainsKey(name))
                {
                    _cacheOffsets[name] = "";
                    _allOffsets[name] = new OffsetItem { Name = name, Value = "", Type = "Cache" };
                    added++;
                }
            }
        }
        return added;
    }


    private void TryFetchVersionSpecific(string version)
    {
        string cachedPath = Path.Combine(OffsetsCacheDir, $"{version}.json");
        if (File.Exists(cachedPath))
        {
            try
            {
                string cachedJson = File.ReadAllText(cachedPath);
                using var doc = JsonDocument.Parse(cachedJson);
                if (doc.RootElement.TryGetProperty("offsets", out JsonElement offsetsElem)
                    && offsetsElem.ValueKind == JsonValueKind.Object
                    && offsetsElem.EnumerateObject().Count() > 100)
                {
                    ParseOffsetsFromProps(offsetsElem);
                    _fetched = true;
                    return;
                }
            }
            catch { }
        }


        try
        {
            string url = $"https://offsets.imtheo.lol/{version}/fflags.hpp";
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            string raw = client.GetStringAsync(url).GetAwaiter().GetResult();
            if (raw.Contains("uintptr_t") && raw.Length > 1000)
            {
                ParseImtheoContent(raw);
                CacheToJson(version, cachedPath);
                _fetched = true;
            }
        }
        catch { }
    }


    private void TryFetchFallback()
    {
        try
        {
            string url = "https://offsets.imtheo.lol/fflags.hpp";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            string raw = client.GetStringAsync(url).GetAwaiter().GetResult();
            if (raw.Contains("uintptr_t") && raw.Length > 1000)
            {
                ParseImtheoContent(raw);
                _fetched = true;
            }
        }
        catch { }
    }


    private void ParseImtheoContent(string content)
    {
        string currentNamespace = "";
        int braceDepth = 0;
        int fflagsDepth = -1;
        int fflagListDepth = -1;
        int count = 0;


        foreach (string rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("//") || line.StartsWith("#") || line.StartsWith("/*") || line.StartsWith("*"))
            {
                if (line.Contains("ClientVersion"))
                {
                    var cvMatch = Regex.Match(line, @"ClientVersion\s*=\s*""([^""]+)""");
                    if (cvMatch.Success)
                    {
                        _currentVersion = cvMatch.Groups[1].Value;
                        _cacheOffsets["ClientVersion"] = _currentVersion;
                        _allOffsets["ClientVersion"] = new OffsetItem { Name = "ClientVersion", Value = _currentVersion, Type = "Cache" };
                    }
                }
                continue;
            }


            var nsMatch = NamespaceRegex.Match(line);
            if (nsMatch.Success)
            {
                string ns = nsMatch.Groups[1].Value;
                if (ns == "FFlagList") { fflagListDepth = braceDepth; currentNamespace = ns; }
                else if (ns == "FFlags") { fflagsDepth = braceDepth; currentNamespace = ns; }
                else { currentNamespace = ns; }
            }


            foreach (char c in line)
            {
                if (c == '{') braceDepth++;
                else if (c == '}')
                {
                    braceDepth--;
                    if (fflagsDepth >= 0 && braceDepth <= fflagsDepth) { fflagsDepth = -1; currentNamespace = ""; }
                    if (fflagListDepth >= 0 && braceDepth <= fflagListDepth) { fflagListDepth = -1; currentNamespace = ""; }
                }
            }


            var m = Regex.Match(line, @"inline\s+constexpr\s+uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+)");
            if (!m.Success)
                m = Regex.Match(line, @"uintptr_t\s+(\w+)\s*=\s*(0x[0-9A-Fa-f]+)");
            if (!m.Success)
                m = Regex.Match(line, @"(\w+)\s*=\s*(0x[0-9A-Fa-f]+)");


            if (m.Success)
            {
                string name = m.Groups[1].Value;
                string hex = m.Groups[2].Value;


                if (fflagListDepth >= 0)
                {
                    _cacheOffsets[name] = hex;
                    _allOffsets[name] = new OffsetItem { Name = name, Value = hex, Type = "CacheOffset" };
                }
                else
                {
                    _memoryOffsets[name] = hex;
                    _allOffsets[name] = new OffsetItem { Name = name, Value = hex, Type = "Memory" };
                    count++;
                }
            }
        }


        _totalFromLink = count;
    }


    private void ParseOffsetsFromProps(JsonElement offsetsElem)
    {
        foreach (var prop in offsetsElem.EnumerateObject())
        {
            string name = prop.Name;
            string hexVal = prop.Value.GetString() ?? "0x0";
            if (name == "ClientVersion" || name.StartsWith("FFlagList") || name == "Pointer" || name == "ToFlag" || name == "ToValue")
            {
                _cacheOffsets[name] = hexVal;
                _allOffsets[name] = new OffsetItem { Name = name, Value = hexVal, Type = "CacheOffset" };
            }
            else
            {
                _memoryOffsets[name] = hexVal;
                _allOffsets[name] = new OffsetItem { Name = name, Value = hexVal, Type = "Memory" };
            }
        }
        _totalFromLink = _memoryOffsets.Count;
    }


    private void CacheToJson(string version, string cachePath)
    {
        try
        {
            var allData = new Dictionary<string, string>();
            foreach (var kv in _cacheOffsets) allData[kv.Key] = kv.Value;
            foreach (var kv in _memoryOffsets) allData[kv.Key] = kv.Value;


            var obj = new
            {
                version = version,
                cacheCount = _cacheOffsets.Count,
                memoryCount = _memoryOffsets.Count,
                offsets = allData
            };
            string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            string tmpPath = cachePath + ".tmp";
            File.WriteAllText(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, cachePath, true);
        }
        catch { }
    }


    public static string GetRunningRobloxVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "process where name=\"RobloxPlayerBeta.exe\" get ExecutablePath",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string path = line.Trim();
                    if (path.Length > 0 && File.Exists(path))
                    {
                        string parent = Path.GetFileName(Path.GetDirectoryName(path));
                        var m = Regex.Match(parent, @"version-([a-f0-9]{16})");
                        if (m.Success)
                            return m.Groups[0].Value;
                    }
                }
            }
        }
        catch { }


        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Roblox\Environments\roblox-player");
            if (key != null)
            {
                object val = key.GetValue("VersionFolder");
                if (val != null)
                {
                    var m = Regex.Match(val.ToString() ?? "", @"version-([a-f0-9]{16})");
                    if (m.Success)
                        return m.Groups[0].Value;
                }
            }
        }
        catch { }


        string versionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox", "Versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (string entry in Directory.GetDirectories(versionsDir))
            {
                var m = Regex.Match(Path.GetFileName(entry), @"version-([a-f0-9]{16})");
                if (m.Success && File.Exists(Path.Combine(entry, "RobloxPlayerBeta.exe")))
                    return m.Groups[0].Value;
            }
        }


        return "";
    }


    public Dictionary<string, string> GetAllMemoryOffsets() => new(_memoryOffsets);
    public Dictionary<string, string> GetAllCacheOffsets()
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in _cacheOffsets)
            result[kv.Key] = kv.Value;
        return result;
    }
    public int MemoryOffsetCount => _memoryOffsets.Count;
    public int CacheOffsetCount => _cacheOffsets.Count;


    public void LoadCachedOffsets()
    {
        if (_fetched) return;


        string version = string.IsNullOrEmpty(_currentVersion) ? GetRunningRobloxVersion() : _currentVersion;
        if (string.IsNullOrEmpty(version)) return;


        string cachedPath = Path.Combine(OffsetsCacheDir, $"{version}.json");
        if (!File.Exists(cachedPath)) return;


        try
        {
            string json = File.ReadAllText(cachedPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("offsets", out JsonElement offsetsElem))
            {
                foreach (var prop in offsetsElem.EnumerateObject())
                {
                    string name = prop.Name;
                    string hexVal = prop.Value.GetString() ?? "0x0";
                    if (name == "ClientVersion" || name == "Pointer" || name == "ToFlag" || name == "ToValue")
                    {
                        _cacheOffsets[name] = hexVal;
                        _allOffsets[name] = new OffsetItem { Name = name, Value = hexVal, Type = "CacheOffset" };
                    }
                    else
                    {
                        _memoryOffsets[name] = hexVal;
                        _allOffsets[name] = new OffsetItem { Name = name, Value = hexVal, Type = "Memory" };
                    }
                }
                _fetched = true;
            }
        }
        catch { }
    }


    public List<OffsetItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _allOffsets.Values.ToList();


        string q = query.ToLowerInvariant();
        return _allOffsets.Values
            .Where(o => o.Name.ToLowerInvariant().Contains(q) || o.Value.ToLowerInvariant().Contains(q))
            .OrderBy(o => o.Name)
            .ToList();
    }


    public bool RefetchForRunningVersion()
    {
        string version = GetRunningRobloxVersion();
        if (string.IsNullOrEmpty(version)) return false;
        if (version == _currentVersion && _fetched && _memoryOffsets.Count > 100) return true;


        _fetched = false;
        _currentVersion = version;
        _memoryOffsets.Clear();
        _allOffsets.Clear();


        try
        {
            string url = $"https://offsets.imtheo.lol/{version}/fflags.hpp";
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            string raw = client.GetStringAsync(url).GetAwaiter().GetResult();
            if (raw.Contains("uintptr_t") && raw.Length > 1000)
            {
                ParseImtheoContent(raw);
                string cachedPath = Path.Combine(OffsetsCacheDir, $"{version}.json");
                CacheToJson(version, cachedPath);
                _fetched = true;
                return true;
            }
        }
        catch { }


        string cachedPath2 = Path.Combine(OffsetsCacheDir, $"{version}.json");
        if (File.Exists(cachedPath2))
        {
            try
            {
                string json = File.ReadAllText(cachedPath2);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("offsets", out JsonElement offsetsElem) && offsetsElem.ValueKind == JsonValueKind.Object)
                {
                    ParseOffsetsFromProps(offsetsElem);
                    _fetched = true;
                    return true;
                }
            }
            catch { }
        }


        return false;
    }


    public Dictionary<string, string> GetOffsetDict()
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in _memoryOffsets)
            result[kv.Key] = kv.Value;
        return result;
    }
}
