using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;


namespace LeitostrapV7.Core;


public class CacheMethod
{
    private static CacheMethod? _instance;
    public static CacheMethod Instance => _instance ??= new CacheMethod();


    private Thread? _watchdogThread;
    private volatile bool _watchdogRunning;
    private Dictionary<string, string> _flags = new();
    private volatile int _totalCacheFlags;
    private static readonly string FlagCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "Roblox", "cache", "flag_cache.dat");


    public bool IsRunning => _watchdogRunning;
    public bool IsProxyRunning => ProxyService.Instance.IsRunning;


    public int ActiveFlagsCount => _totalCacheFlags > 0 ? _totalCacheFlags : _flags.Count;


    private CacheMethod() { }


    public static bool IsAdmin()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }


    public static void ElevateAndRelaunch()
    {
        try
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exe)) return;


            var psi = new ProcessStartInfo(exe)
            {
                Verb = "runas",
                UseShellExecute = true
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch { }
    }


    public (bool ok, string msg) StartNoProxy(Dictionary<string, string> flags)
    {
        if (flags == null || flags.Count == 0)
            return (false, "No flags to inject");


        _flags = new Dictionary<string, string>(flags);
        _watchdogRunning = true;


        ConsoleService.Instance.Log("Starting cache injection (no proxy)...", "Info");


        bool primed = PrimeCache(_flags, makeReadonly: false);
        if (primed)
            ConsoleService.Instance.Log($"Cache file primed with {_totalCacheFlags:N0} flags", "Success");
        else
            ConsoleService.Instance.Log("Cache file not found yet, watchdog will retry", "Warning");


        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "LeitostrapCacheWatchdog" };
        _watchdogThread.Start();


        string msg = $"Cache watchdog active ({_flags.Count:N0} imported flags, no proxy)";
        ConsoleService.Instance.Log(msg, "Success");
        return (true, msg);
    }


    public (bool ok, string msg) Start(Dictionary<string, string> flags)
    {
        if (flags == null || flags.Count == 0)
            return (false, "No flags to inject");


        _flags = new Dictionary<string, string>(flags);
        _watchdogRunning = true;


        ConsoleService.Instance.Log("Starting cache injection...", "Info");


        bool proxyStarted = false;
        try
        {
            ProxyService.Instance.Log = (tag, msg) => ConsoleService.Instance.Log($"[{tag}] {msg}", "Info");
            proxyStarted = ProxyService.Instance.Start(flags);
            if (proxyStarted)
            {
                ConsoleService.Instance.Log("HTTPS proxy active — intercepting Roblox settings", "Success");
                ProxyService.Instance.DetectFirewall();
                ProxyService.Instance.VerifyHostsActive();
                var (selfOk, selfMsg) = ProxyService.Instance.RunSelfTest();
                if (!selfOk)
                {
                    ConsoleService.Instance.Log($"Proxy self-test failed: {selfMsg}", "Error");
                    ConsoleService.Instance.Log("Continuing with cache file only — fix firewall/AV and retry for live interception", "Warning");
                    try { ProxyService.Instance.Stop(); } catch { }
                    proxyStarted = false;
                }
            }
            else
                ConsoleService.Instance.Log("Proxy unavailable — using file cache only", "Warning");
        }
        catch (Exception ex)
        {
            ConsoleService.Instance.Log($"Proxy skipped: {ex.Message}", "Warning");
        }


        bool primed = PrimeCache(_flags, makeReadonly: false);
        if (primed)
            ConsoleService.Instance.Log($"Cache file primed: your {_flags.Count:N0} imported flags merged into Roblox's flag cache", "Success");
        else
            ConsoleService.Instance.Log("Waiting for Roblox to create flag_cache.dat...", "Info");


        _watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "LeitostrapCacheWatchdog" };
        _watchdogThread.Start();


        string msg = proxyStarted
            ? $"Proxy + cache watchdog active ({_flags.Count:N0} imported flags)"
            : $"Cache watchdog active ({_flags.Count:N0} imported flags)";


        ConsoleService.Instance.Log(msg, "Success");
        return (true, msg);
    }


    public bool UpdateFlags(Dictionary<string, string> flags)
    {
        _flags = new Dictionary<string, string>(flags);
        if (ProxyService.Instance.IsRunning)
            ProxyService.Instance.UpdateFlags(flags);
        return true;
    }


    public void Stop()
    {
        _watchdogRunning = false;
        _watchdogThread?.Join(2000);
        _watchdogThread = null;
        ProxyService.Instance.Stop();
        ConsoleService.Instance.Log("Cache + proxy stopped", "Info");
    }


    private void WatchdogLoop()
    {
        int consecutiveFails = 0;


        while (_watchdogRunning)
        {
            Thread.Sleep(500);
            try
            {
                if (_flags.Count == 0) continue;


                bool running = ProcessService.Instance.IsRunning();
                if (!running) continue;


                bool verified = VerifyAndFixCache(_flags);
                if (verified)
                {
                    consecutiveFails = 0;
                }
                else
                {
                    consecutiveFails++;
                    if (consecutiveFails >= 3)
                    {
                        PrimeCache(_flags, makeReadonly: true);
                        consecutiveFails = 0;
                    }
                }
            }
            catch { }
        }
        ConsoleService.Instance.Log("Cache watchdog loop ended", "Info");
    }


    private bool PrimeCache(Dictionary<string, string> flags, bool makeReadonly)
    {
        if (!File.Exists(FlagCachePath)) return false;
        try
        {
            if (makeReadonly) TryMakeWritable(FlagCachePath);


            byte[] raw = File.ReadAllBytes(FlagCachePath);
            if (raw.Length < 5) return false;


            int sigLen = BitConverter.ToInt32(raw, 0);
            int compressionOffset = 4 + sigLen;
            int payloadOffset = compressionOffset + 1;
            if (payloadOffset >= raw.Length || raw[compressionOffset] != 0) return false;


            string payloadJson = Encoding.UTF8.GetString(raw, payloadOffset, raw.Length - payloadOffset);
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("applicationSettings", out _)) return false;


            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                doc.RootElement.GetProperty("applicationSettings").GetRawText());
            if (dict == null) return false;


            foreach (var kv in flags)
            {
                using var valDoc = JsonDocument.Parse($"\"{kv.Value.Replace("\"", "\\\"")}\"");
                dict[kv.Key] = valDoc.RootElement.Clone();
            }


            _totalCacheFlags = dict.Count;


            var newPayload = new Dictionary<string, object> { ["applicationSettings"] = dict };
            string updatedJson = JsonSerializer.Serialize(newPayload, new JsonSerializerOptions { WriteIndented = false });
            byte[] updatedJsonBytes = Encoding.UTF8.GetBytes(updatedJson);
            byte[] updated = new byte[payloadOffset + updatedJsonBytes.Length];
            Buffer.BlockCopy(raw, 0, updated, 0, payloadOffset);
            Buffer.BlockCopy(updatedJsonBytes, 0, updated, payloadOffset, updatedJsonBytes.Length);


            string tmpPath = FlagCachePath + $".{Environment.ProcessId}.tmp";
            File.WriteAllBytes(tmpPath, updated);
            File.Move(tmpPath, FlagCachePath, true);
            try { File.Delete(tmpPath); } catch { }


            if (makeReadonly) TryMakeReadonly(FlagCachePath);


            return true;
        }
        catch { return false; }
    }


    private bool VerifyAndFixCache(Dictionary<string, string> flags)
    {
        if (!File.Exists(FlagCachePath)) return false;
        try
        {
            byte[] raw = File.ReadAllBytes(FlagCachePath);
            if (raw.Length < 5) return false;


            int sigLen = BitConverter.ToInt32(raw, 0);
            int compressionOffset = 4 + sigLen;
            int payloadOffset = compressionOffset + 1;
            if (payloadOffset >= raw.Length || raw[compressionOffset] != 0) return false;


            string payloadJson = Encoding.UTF8.GetString(raw, payloadOffset, raw.Length - payloadOffset);
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("applicationSettings", out var appSettings)) return false;


            _totalCacheFlags = appSettings.EnumerateObject().Count();


            bool allOk = true;
            foreach (var kv in flags)
            {
                if (appSettings.TryGetProperty(kv.Key, out var current))
                {
                    string currentVal = current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.GetRawText();
                    if (currentVal.Trim() != kv.Value.Trim()) { allOk = false; break; }
                }
                else { allOk = false; break; }
            }
            if (allOk) return true;


            return PrimeCache(flags, makeReadonly: true);
        }
        catch { return false; }
    }


    private static void TryMakeWritable(string path)
    {
        try { File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly); } catch { }
    }


    private static void TryMakeReadonly(string path)
    {
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly); } catch { }
    }
}
