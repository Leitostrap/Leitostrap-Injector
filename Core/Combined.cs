using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LeitostrapV7.Core;

public class Combined
{
    private static Combined? _instance;
    public static Combined Instance => _instance ??= new Combined();

    private Combined() { }

    private volatile bool _injecting;
    public bool IsInjecting => _injecting;

    public enum InjectionMethod
    {
        Combined,
        CacheMethod,
        MemoryOffsets,
        MemoryOffsetless
    }

    public bool IsProxyRunning => ProxyService.Instance.IsRunning;

    public Action<string, string>? OnStatusUpdate { get; set; }

    public async Task<(bool ok, string msg)> InjectAsync(
        Dictionary<string, string> flags,
        InjectionMethod method,
        Action<int, int>? progress = null)
    {
        if (_injecting)
            return (false, "Injection already in progress");

        if (flags == null || flags.Count == 0)
            return (false, "No flags to inject");

        var flatDict = new Dictionary<string, string>();
        foreach (var kv in flags)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            flatDict[kv.Key.Trim()] = kv.Value.Trim();
        }

        if (flatDict.Count == 0)
            return (false, "No flags with values to apply");

        _injecting = true;
        try
        {
            ConsoleService.Instance.Log($"Starting injection: {method} with {flatDict.Count} flags", "Info");

            switch (method)
            {
                case InjectionMethod.Combined:
                    return await InjectCombined(flatDict, progress);

                case InjectionMethod.CacheMethod:
                    return await InjectProxy(flatDict);

                case InjectionMethod.MemoryOffsets:
                    return await InjectMemory(flatDict, preferOffsetless: false, progress);

                case InjectionMethod.MemoryOffsetless:
                    return await InjectMemory(flatDict, preferOffsetless: true, progress);

                default:
                    return await InjectMemory(flatDict, preferOffsetless: false, progress);
            }
        }
        catch (Exception ex)
        {
            ConsoleService.Instance.Log($"Injection fatal error: {ex.Message}", "Error");
            OnStatusUpdate?.Invoke($"Error: {ex.Message}", "error");
            return (false, $"Injection error: {ex.Message}");
        }
        finally
        {
            _injecting = false;
        }
    }

    private async Task<(bool ok, string msg)> InjectCombined(
        Dictionary<string, string> flags,
        Action<int, int>? progress)
    {
        ConsoleService.Instance.Log("Combined: Starting HTTPS proxy...", "Info");
        OnStatusUpdate?.Invoke("Starting proxy...", "injecting");

        var proxyFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);

        bool proxyOk = false;
        await Task.Run(() =>
        {
            ProxyService.Instance.Log = (tag, m) => ConsoleService.Instance.Log($"[{tag}] {m}", "Info");
            proxyOk = ProxyService.Instance.Start(proxyFlags);
        });

        if (!proxyOk)
        {
            OnStatusUpdate?.Invoke("Proxy failed to start", "error");
            return (false, "Proxy failed to start. Run as Administrator.");
        }

        ConsoleService.Instance.Log($"Combined: Proxy started with {proxyFlags.Count:N0} imported flags...", "Info");

        if (ProcessService.Instance.IsRunning())
        {
            ConsoleService.Instance.Log("Combined: Killing Roblox to relaunch with proxy...", "Info");
            OnStatusUpdate?.Invoke("Closing Roblox...", "waiting");
            ProcessService.Instance.KillRoblox();
            await Task.Delay(2000);
        }

        ConsoleService.Instance.Log("Combined: Launching Roblox...", "Info");
        OnStatusUpdate?.Invoke("Launching Roblox...", "waiting");
        if (!ProcessService.Instance.LaunchRoblox())
        {
            ConsoleService.Instance.Log("Combined: Launch failed, falling back to memory-only injection...", "Warning");
            ProxyService.Instance.Stop();
            return await InjectMemory(flags, preferOffsetless: false, progress);
        }

        await WaitForRobloxProcessAsync(20000);

        ConsoleService.Instance.Log("Combined: Attempting memory injection (up to 15 tries)...", "Info");
        OnStatusUpdate?.Invoke("Attempting memory injection...", "detecting");

        bool memoryOk = false;
        string memoryMsg = "";
        int maxAttempts = 15;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!ProcessService.Instance.IsRunning())
            {
                ConsoleService.Instance.Log($"Combined: Attempt {attempt}/{maxAttempts} - Roblox not running, waiting...", "Info");
                await Task.Delay(2000);
                continue;
            }

            ConsoleService.Instance.Log($"Combined: Attempt {attempt}/{maxAttempts} - trying memory injection...", "Info");

            try
            {
                var mem = MemoryOffsets.Instance;
                var offsetService = OffsetService.Instance;

                bool offsetsOk = await Task.Run(() => offsetService.RefetchForRunningVersion());
                if (!offsetsOk)
                    await Task.Run(() => offsetService.LoadCachedOffsets());

                var offsets = offsetService.GetAllMemoryOffsets();
                if (offsets.Count == 0)
                {
                    ConsoleService.Instance.Log($"Combined: Attempt {attempt} - no offsets available, retrying...", "Info");
                    await Task.Delay(1500);
                    continue;
                }

                var longOffsets = new Dictionary<string, long>();
                foreach (var kv in offsets)
                {
                    if (kv.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (long.TryParse(kv.Value[2..], System.Globalization.NumberStyles.HexNumber, null, out long val))
                            longOffsets[kv.Key] = val;
                    }
                    else if (long.TryParse(kv.Value, out long val))
                        longOffsets[kv.Key] = val;
                }

                if (longOffsets.Count == 0)
                {
                    ConsoleService.Instance.Log($"Combined: Attempt {attempt} - offsets could not be parsed, retrying...", "Info");
                    await Task.Delay(1500);
                    continue;
                }

                mem.SetOffsets(longOffsets);

                if (!mem.IsAttached)
                    mem.AttachCore();

                if (!mem.IsAttached)
                {
                    ConsoleService.Instance.Log($"Combined: Attempt {attempt} - not attached, retrying...", "Info");
                    await Task.Delay(1500);
                    continue;
                }

                var (ok, fail) = mem.InjectFlags(flags);
                progress?.Invoke(ok, ok + fail);

                if (ok > 0)
                {
                    memoryOk = true;
                    memoryMsg = $"Memory injection succeeded: {ok}/{ok + fail} flags";
                    ConsoleService.Instance.Log($"Combined: {memoryMsg}", "Success");
                    break;
                }

                ConsoleService.Instance.Log($"Combined: Attempt {attempt} - 0 flags written, retrying...", "Info");
            }
            catch (Exception ex)
            {
                ConsoleService.Instance.Log($"Combined: Attempt {attempt} - error: {ex.Message}", "Info");
            }

            await Task.Delay(1500);
        }

        if (memoryOk)
        {
            if (ProxyService.Instance.SuccessfulInjections == 0 && ProxyService.Instance.SettingsRequests == 0)
            {
                ConsoleService.Instance.Log("Combined: Memory succeeded, giving proxy a window to catch the settings fetch...", "Info");
                int proxyWaitMs = 0;
                while (proxyWaitMs < 15000)
                {
                    await Task.Delay(500);
                    proxyWaitMs += 500;
                    if (ProxyService.Instance.SuccessfulInjections > 0 || ProxyService.Instance.SettingsRequests > 0)
                    {
                        await Task.Delay(2500);
                        break;
                    }
                }
            }

            int verified = await VerifyMemoryFlagsAsync(flags);
            ConsoleService.Instance.Log($"Combined: Memory verification {verified}/{flags.Count} flags confirmed", "Info");

            ConsoleService.Instance.Log("Combined: Memory injection succeeded, releasing proxy (memory is primary)...", "Info");
            OnStatusUpdate?.Invoke("Releasing proxy, memory is primary...", "injecting");
            ProxyService.Instance.Stop();

            var mem = MemoryOffsets.Instance;
            mem.StartWatchdog(flags);

            if (verified > 0)
            {
                string msg = $"Combined: Memory injection verified ({verified}/{flags.Count} flags), proxy released";
                OnStatusUpdate?.Invoke(msg, "done");
                return (true, msg);
            }

            string fallbackMsg = "Combined: Memory injected but unverified, proxy released, cache watchdog active";
            ConsoleService.Instance.Log(fallbackMsg, "Warning");
            StartCacheWatchdogQuiet(flags);
            OnStatusUpdate?.Invoke(fallbackMsg, "done");
            return (true, fallbackMsg);
        }

        ConsoleService.Instance.Log("Combined: Memory injection failed after retries, trying offsetless scan...", "Warning");
        OnStatusUpdate?.Invoke("Trying offsetless scan...", "detecting");

        var procsAfter = Process.GetProcessesByName("RobloxPlayerBeta");
        if (procsAfter.Length > 0)
        {
            var offsetless = MemoryOffsetless.Instance;
            bool attached = false;
            try
            {
                attached = offsetless.Attach(procsAfter[0].Id);
            }
            catch { }

            if (attached && offsetless.FindSingleton() != 0)
            {
                try
                {
                    var injResult = offsetless.InjectAll(flags);
                    progress?.Invoke(injResult.written, injResult.total);
                    ConsoleService.Instance.Log($"Combined: Offsetless result: {injResult.msg}", injResult.ok ? "Success" : "Warning");

                    if (injResult.ok)
                    {
                        ProxyService.Instance.Stop();
                        offsetless.StartWatchdog(flags);
                        string msg = $"Combined: Offsetless injection succeeded: {injResult.msg}. Proxy released.";
                        OnStatusUpdate?.Invoke(msg, "done");
                        return (true, msg);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleService.Instance.Log($"Combined: Offsetless injection error: {ex.Message}", "Warning");
                }
            }
        }

        ConsoleService.Instance.Log("Combined: All memory paths failed, keeping proxy active as fallback", "Info");
        StartCacheWatchdogQuiet(flags);

        string finalMsg = $"Combined: Memory injection failed, proxy stays active as fallback ({flags.Count} flags).";
        OnStatusUpdate?.Invoke(finalMsg, "done");
        return (true, finalMsg);
    }

    private async Task<(bool ok, string msg)> InjectProxy(Dictionary<string, string> flags)
    {
        ConsoleService.Instance.Log("Proxy: Starting HTTPS proxy...", "Info");
        OnStatusUpdate?.Invoke("Starting proxy...", "injecting");

        var proxyFlags = new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);

        bool proxyOk = false;
        await Task.Run(() =>
        {
            ProxyService.Instance.Log = (tag, m) => ConsoleService.Instance.Log($"[{tag}] {m}", "Info");
            proxyOk = ProxyService.Instance.Start(proxyFlags);
        });

        if (!proxyOk)
        {
            OnStatusUpdate?.Invoke("Proxy failed to start", "error");
            return (false, "Proxy failed to start. Run as Administrator.");
        }

        ConsoleService.Instance.Log($"Proxy: Proxy started with {proxyFlags.Count:N0} imported flags...", "Info");

        ConsoleService.Instance.Log("Proxy: Running self-test and diagnostics before launching Roblox...", "Info");
        ProxyService.Instance.DetectFirewall();
        ProxyService.Instance.VerifyHostsActive();
        var (selfOk, selfMsg) = ProxyService.Instance.RunSelfTest();
        if (!selfOk)
        {
            ConsoleService.Instance.Log("Proxy: Self-test failed, aborting before launch. Fix the issue and try again.", "Error");
            OnStatusUpdate?.Invoke("Proxy self-test failed", "error");
            ProxyService.Instance.Stop();
            return (false, selfMsg);
        }

        if (ProcessService.Instance.IsRunning())
        {
            ConsoleService.Instance.Log("Proxy: Killing Roblox to relaunch with proxy...", "Info");
            OnStatusUpdate?.Invoke("Closing Roblox...", "waiting");
            ProcessService.Instance.KillRoblox();
            await Task.Delay(2000);
        }

        ConsoleService.Instance.Log("Proxy: Launching Roblox...", "Info");
        OnStatusUpdate?.Invoke("Launching Roblox...", "waiting");
        if (!ProcessService.Instance.LaunchRoblox())
        {
            ProxyService.Instance.Stop();
            OnStatusUpdate?.Invoke("Failed to launch Roblox", "error");
            return (false, "Failed to launch Roblox.");
        }

        ConsoleService.Instance.Log("Proxy: Waiting for Roblox to load and fetch settings...", "Info");
        OnStatusUpdate?.Invoke("Waiting for Roblox to load...", "detecting");

        await WaitForRobloxProcessAsync(30000);

        int waitMs = 120000;
        int waitedMs = 0;
        int idleMs = 0;
        int lastRequests = 0;
        while (waitedMs < waitMs)
        {
            await Task.Delay(500);
            waitedMs += 500;

            if (ProxyService.Instance.SuccessfulInjections > 0)
            {
                await Task.Delay(2500);
                break;
            }

            int nowRequests = ProxyService.Instance.SettingsRequests;
            if (nowRequests == lastRequests)
                idleMs += 500;
            else
            {
                idleMs = 0;
                lastRequests = nowRequests;
            }

            if (nowRequests > 0 && idleMs >= 20000)
                break;
        }

        int intercepted = ProxyService.Instance.SettingsRequests;
        int injections = ProxyService.Instance.SuccessfulInjections;

        ConsoleService.Instance.Log($"Proxy: Intercepted {intercepted} settings request(s), injected {injections} response(s)", "Info");

        ConsoleService.Instance.Log("Proxy: Releasing proxy, starting flag cache watchdog...", "Info");

        ProxyService.Instance.Stop();

        StartCacheWatchdogQuiet(flags);

        if (injections > 0)
        {
            string msg = $"Proxy: FFlags injected through proxy before Roblox fully opened ({injections} response(s), {flags.Count} flags).";
            ConsoleService.Instance.Log(msg, "Success");
            OnStatusUpdate?.Invoke(msg, "done");
            return (true, msg);
        }

        if (intercepted == 0)
        {
            string noReqMsg = "Proxy: Roblox did not request settings through the proxy. " +
                "Flags were not applied - try again or check your antivirus/firewall.";
            ConsoleService.Instance.Log(noReqMsg, "Error");
            OnStatusUpdate?.Invoke(noReqMsg, "error");
            return (false, noReqMsg);
        }

        string failMsg = "Proxy: Settings requests were intercepted but injection did not complete. Check console logs.";
        ConsoleService.Instance.Log(failMsg, "Error");
        OnStatusUpdate?.Invoke(failMsg, "error");
        return (false, failMsg);
    }

    private async Task<(bool ok, string msg)> InjectMemory(
        Dictionary<string, string> flags,
        bool preferOffsetless,
        Action<int, int>? progress)
    {
        string methodName = preferOffsetless ? "MemoryOffsetless" : "MemoryOffsets";

        bool alreadyRunning = ProcessService.Instance.IsRunning();

        if (!alreadyRunning)
        {
            OnStatusUpdate?.Invoke("Launching Roblox...", "waiting");
            ConsoleService.Instance.Log($"{methodName}: Launching Roblox...", "Info");
            if (!ProcessService.Instance.LaunchRoblox())
                return (false, "Failed to launch Roblox. Make sure it's installed.");
            await Task.Delay(5000);
        }
        else
        {
            ConsoleService.Instance.Log($"{methodName}: Roblox already running, attaching directly...", "Info");
        }

        progress?.Invoke(0, flags.Count);
        if (!alreadyRunning)
        {
            OnStatusUpdate?.Invoke("Waiting for Roblox window...", "waiting");
            ConsoleService.Instance.Log($"{methodName}: Injecting as soon as Roblox opens (2-3s target)...", "Info");
        }
        else
        {
            OnStatusUpdate?.Invoke("Attaching to Roblox...", "detecting");
            ConsoleService.Instance.Log($"{methodName}: Attaching to running Roblox...", "Info");
        }

        var startTime = DateTime.UtcNow;
        bool ready = false;
        int waitMs = alreadyRunning ? 1000 : 2000;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < 45000)
        {
            if (!ProcessService.Instance.IsRunning())
            {
                OnStatusUpdate?.Invoke("Waiting for Roblox to start...", "waiting");
                await Task.Delay(500);
                continue;
            }

            double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

            if (elapsed * 1000 < waitMs)
            {
                await Task.Delay(250);
                continue;
            }

            OnStatusUpdate?.Invoke($"Trying to attach... ({(int)elapsed}s)", "detecting");

            try
            {
                if (preferOffsetless)
                {
                    var procs = Process.GetProcessesByName("RobloxPlayerBeta");
                    if (procs.Length == 0) { await Task.Delay(1000); continue; }

                    var offsetless = MemoryOffsetless.Instance;
                    if (!offsetless.Attach(procs[0].Id)) { await Task.Delay(1500); continue; }
                    if (offsetless.FindSingleton() == 0) { await Task.Delay(2000); continue; }

                    ready = true;
                    ConsoleService.Instance.Log($"Ready after {(int)elapsed}s (offsetless)", "Success");
                    break;
                }
                else
                {
                    var mem = MemoryOffsets.Instance;
                    if (!mem.IsAttached)
                        mem.AttachCore();

                    if (!mem.IsAttached) { await Task.Delay(1500); continue; }

                    ready = true;
                    ConsoleService.Instance.Log($"Ready after {(int)elapsed}s (memory offsets)", "Success");
                    break;
                }
            }
            catch { await Task.Delay(2000); continue; }
        }

        if (!ready)
        {
            OnStatusUpdate?.Invoke("Timed out waiting for Roblox", "error");
            ConsoleService.Instance.Log($"{methodName}: Timed out", "Error");
            return (false, $"{methodName}: Timed out. Try running as Administrator.");
        }

        if (!preferOffsetless)
        {
            OnStatusUpdate?.Invoke("Loading offsets...", "detecting");
            ConsoleService.Instance.Log($"{methodName}: Preparing offsets before injection...", "Info");
            try
            {
                var offsetServicePre = OffsetService.Instance;
                var preloadTask = Task.Run(() => offsetServicePre.RefetchForRunningVersion());
                if (preloadTask.Wait(20000))
                {
                    if (!preloadTask.Result)
                        offsetServicePre.LoadCachedOffsets();
                }
                else
                {
                    ConsoleService.Instance.Log($"{methodName}: Offset refetch slow, using cached offsets...", "Warning");
                    offsetServicePre.LoadCachedOffsets();
                }
            }
            catch { }
        }

        OnStatusUpdate?.Invoke("Injecting flags...", "injecting");
        ConsoleService.Instance.Log($"{methodName}: Injecting {flags.Count} flags...", "Info");
        progress?.Invoke(0, flags.Count);

        return await Task.Run(() =>
        {
            try
            {
                if (preferOffsetless)
                {
                    var procs = Process.GetProcessesByName("RobloxPlayerBeta");
                    if (procs.Length == 0) return (false, "Roblox process not found");

                    var offsetless = MemoryOffsetless.Instance;
                    var injResult = offsetless.InjectAll(flags);
                    progress?.Invoke(injResult.written, injResult.total);

                    if (injResult.ok)
                    {
                        ConsoleService.Instance.Log($"{methodName}: {injResult.msg}", "Success");
                        OnStatusUpdate?.Invoke($"{injResult.written}/{injResult.total} flags injected", "done");
                    }
                    else
                    {
                        ConsoleService.Instance.Log($"{methodName}: {injResult.msg}", "Error");
                        OnStatusUpdate?.Invoke(injResult.msg, "error");
                    }
                    return (injResult.ok, injResult.msg);
                }
                else
                {
                    var mem = MemoryOffsets.Instance;
                    var offsetService = OffsetService.Instance;

                    ConsoleService.Instance.Log($"{methodName}: Using prepared offsets...", "Info");
                    var offsets = offsetService.GetAllMemoryOffsets();

                    if (offsets.Count == 0)
                    {
                        ConsoleService.Instance.Log($"{methodName}: No offsets available", "Error");
                        return (false, "No memory offsets available for this Roblox version. Try updating Roblox.");
                    }

                    var longOffsets = new Dictionary<string, long>();
                    foreach (var kv in offsets)
                    {
                        if (kv.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (long.TryParse(kv.Value[2..], System.Globalization.NumberStyles.HexNumber, null, out long val))
                                longOffsets[kv.Key] = val;
                        }
                        else if (long.TryParse(kv.Value, out long val))
                            longOffsets[kv.Key] = val;
                    }
                    mem.SetOffsets(longOffsets);

                    ConsoleService.Instance.Log($"{methodName}: Writing {flags.Count} flags via offsets ({longOffsets.Count} offsets loaded)...", "Info");

                    var (ok, fail) = mem.InjectFlags(flags);
                    progress?.Invoke(ok, ok + fail);

                    if (ok > 0)
                    {
                        string msg = $"Injected {ok}/{ok + fail} flags ({fail} failed)";
                        ConsoleService.Instance.Log($"{methodName}: {msg}", "Success");
                        OnStatusUpdate?.Invoke(msg, "done");
                        mem.StartWatchdog(flags);
                        return (true, msg);
                    }

                    string failMsg = $"Failed: 0/{ok + fail} flags injected. Offsets may be outdated for this Roblox version.";
                    ConsoleService.Instance.Log($"{methodName}: {failMsg}", "Error");
                    OnStatusUpdate?.Invoke(failMsg, "error");
                    return (false, failMsg);
                }
            }
            catch (Exception ex)
            {
                ConsoleService.Instance.Log($"{methodName}: Error: {ex.Message}", "Error");
                OnStatusUpdate?.Invoke($"Error: {ex.Message}", "error");
                return (false, $"{methodName} error: {ex.Message}");
            }
        });
    }

    public (bool ok, string msg) Stop()
    {
        try
        {
            MemoryOffsets.Instance.StopWatchdog();
            try { MemoryOffsetless.Instance.Detach(); } catch { }
            try { ProxyService.Instance.Stop(); } catch { }
            try { CacheMethod.Instance.Stop(); } catch { }
            OnStatusUpdate = null;
            return (true, "All injection methods stopped");
        }
        catch (Exception ex)
        {
            return (false, $"Stop error: {ex.Message}");
        }
    }

    public bool NeedsReapply(Dictionary<string, string> flags)
    {
        if (flags == null || flags.Count == 0) return false;

        if (CacheMethod.Instance.IsRunning)
            return false;

        if (MemoryOffsets.Instance.IsAttached)
        {
            foreach (var kv in flags)
            {
                string clean = MemoryOffsets.CleanFlagPrefix(kv.Key);
                if (MemoryOffsets.Instance.ReadInt(0) != 0)
                {
                    var parsed = MemoryOffsets.Instance.ParseFlag(kv.Key, kv.Value);
                    if (parsed.type == "int")
                    {
                        var offsets = OffsetService.Instance.GetAllMemoryOffsets();
                        if (offsets.TryGetValue(clean, out string? hexVal) || offsets.TryGetValue(kv.Key, out hexVal))
                        {
                            if (hexVal.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                                long.TryParse(hexVal[2..], System.Globalization.NumberStyles.HexNumber, null, out long offsetVal))
                            {
                                long address = MemoryOffsets.Instance.ModBase + offsetVal;
                                int current = MemoryOffsets.Instance.ReadInt(address);
                                int expected = (int)parsed.pval;
                                if (current != expected) return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private async Task<bool> WaitForRobloxProcessAsync(int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (ProcessService.Instance.IsRunning()) return true;
            await Task.Delay(500);
            waited += 500;
        }
        return ProcessService.Instance.IsRunning();
    }

    private async Task<int> VerifyMemoryFlagsAsync(Dictionary<string, string> flags)
    {
        return await Task.Run(() =>
        {
            int verified = 0;
            try
            {
                var mem = MemoryOffsets.Instance;
                if (!mem.IsAttached) return 0;

                var offsets = OffsetService.Instance.GetAllMemoryOffsets();
                foreach (var kv in flags)
                {
                    string clean = MemoryOffsets.CleanFlagPrefix(kv.Key);
                    if (!offsets.TryGetValue(clean, out string? hexVal) &&
                        !offsets.TryGetValue(kv.Key, out hexVal))
                        continue;

                    if (!hexVal.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!long.TryParse(hexVal[2..], System.Globalization.NumberStyles.HexNumber, null, out long offsetVal))
                        continue;

                    long address = mem.ModBase + offsetVal;
                    int current = mem.ReadInt(address);

                    var parsed = mem.ParseFlag(kv.Key, kv.Value);
                    if (parsed.type == "bool")
                    {
                        int expected = (kv.Value.Trim().ToLowerInvariant() is "true" or "1" or "yes") ? 1 : 0;
                        if (current == expected) verified++;
                    }
                    else if (parsed.type == "int")
                    {
                        if (current == (int)parsed.pval) verified++;
                    }
                    else
                    {
                        verified++;
                    }
                }
            }
            catch { }
            return verified;
        });
    }

    private void StartCacheWatchdogQuiet(Dictionary<string, string> flags)
    {
        try
        {
            CacheMethod.Instance.StartNoProxy(flags);
        }
        catch (Exception ex)
        {
            ConsoleService.Instance.Log($"Cache watchdog failed to start: {ex.Message}", "Warning");
        }
    }
}
