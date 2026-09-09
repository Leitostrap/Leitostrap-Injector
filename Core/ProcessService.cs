using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;


namespace LeitostrapV7.Core;


public class RobloxProcessInfo
{
    public int PID { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string ExePath { get; set; } = "";
    public DateTime StartTime { get; set; }
    public double MemoryMB { get; set; }
    public string PlaceID { get; set; } = "";
    public string GameName { get; set; } = "";
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string UserID { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string GameThumbUrl { get; set; } = "";
}


public class ProcessService
{
    private static ProcessService? _instance;
    public static ProcessService Instance => _instance ??= new ProcessService();


    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    }) { Timeout = TimeSpan.FromSeconds(8) };


    private string _lastDetectedVersion = "";
    private string _lastDetectedPath = "";
    private string _lastDetectedFolder = "";
    private bool _wasRunning;
    public event Action<string, string, string>? OnRobloxDetected;
    public event Action? OnRobloxClosed;


    private ProcessService() { }


    public async Task<List<RobloxProcessInfo>> FindClientsAsync()
    {
        var clients = new List<RobloxProcessInfo>();
        try
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            if (processes.Length == 0) return clients;


            var (username, displayName, userId) = GetCachedUserInfo();
            string avatarUrl = !string.IsNullOrEmpty(userId) ? await GetAvatarUrlAsync(userId) : "";


            foreach (var proc in processes)
            {
                try
                {
                    var info = new RobloxProcessInfo
                    {
                        PID = proc.Id,
                        Name = proc.ProcessName,
                        Title = proc.MainWindowTitle,
                        StartTime = proc.StartTime,
                        MemoryMB = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 2),
                        ExePath = GetProcessPath(proc),
                        Username = username,
                        DisplayName = displayName,
                        UserID = userId,
                        AvatarUrl = avatarUrl
                    };


                    info.PlaceID = GetPlaceIdFromArgs(proc);
                    if (string.IsNullOrEmpty(info.PlaceID))
                        info.PlaceID = GetPlaceIdFromLogs();


                    if (!string.IsNullOrEmpty(info.PlaceID))
                    {
                        var (gameName, thumbUrl) = await GetGameInfoAsync(info.PlaceID);
                        info.GameName = gameName;
                        info.GameThumbUrl = thumbUrl;
                    }


                    if (string.IsNullOrEmpty(info.GameName))
                    {
                        if (!string.IsNullOrEmpty(info.Title) && info.Title.ToLower() != "roblox")
                        {
                            var titleMatch = Regex.Match(info.Title, @"^(.+?)(?:\s*[-–|]\s*Roblox)?$", RegexOptions.IgnoreCase);
                            info.GameName = titleMatch.Success && titleMatch.Groups[1].Value.Length > 1
                                ? titleMatch.Groups[1].Value.Trim()
                                : info.Title;
                        }
                        else
                            info.GameName = "Unknown Game";
                    }


                    clients.Add(info);
                }
                catch { }
            }
        }
        catch { }
        return clients;
    }


    public List<RobloxProcessInfo> FindClients() => FindClientsAsync().GetAwaiter().GetResult();


    private (string username, string displayName, string userId) GetCachedUserInfo()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appStorage = Path.Combine(localAppData, "Roblox", "LocalStorage", "appStorage.json");
            if (!File.Exists(appStorage)) return ("", "", "");


            var content = File.ReadAllText(appStorage);
            var usernameMatch = Regex.Match(content, @"""Username""\s*:\s*""([^""]+)""");
            var userIdMatch = Regex.Match(content, @"""UserId""\s*:\s*""?(\d+)""?");
            var displayNameMatch = Regex.Match(content, @"""DisplayName""\s*:\s*""([^""]+)""");


            return (
                usernameMatch.Success ? usernameMatch.Groups[1].Value : "",
                displayNameMatch.Success ? displayNameMatch.Groups[1].Value : "",
                userIdMatch.Success ? userIdMatch.Groups[1].Value : ""
            );
        }
        catch { return ("", "", ""); }
    }


    private async Task<string> GetAvatarUrlAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return "";
        try
        {
            var url = $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false";
            var json = JsonDocument.Parse(await _http.GetStringAsync(url));
            if (json.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                return data[0].GetProperty("imageUrl").GetString() ?? "";
        }
        catch { }
        return "";
    }


    private string GetPlaceIdFromArgs(Process proc)
    {
        try
        {
            var cmdLine = GetCommandLineWin32(proc.Id);
            if (!string.IsNullOrEmpty(cmdLine))
            {
                var patterns = new[] {
                    @"-placeid=(\d+)", @"-PlaceId=(\d+)",
                    @"placeid[=\s:]+(\d{5,})", @"placeId[=\s:]+(\d{5,})",
                    @"PlaceID[=\s:]+(\d{5,})", @"place_id[=\s:]+(\d{5,})"
                };
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(cmdLine, pattern, RegexOptions.IgnoreCase);
                    if (match.Success) return match.Groups[1].Value;
                }
            }
        }
        catch { }
        return "";
    }


    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength, ref int returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out int lpNumberOfBytesRead);


    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }


    private string GetCommandLineWin32(int pid)
    {
        try
        {
            const uint PROCESS_QUERY_INFORMATION = 0x0400;
            const uint PROCESS_VM_READ = 0x0010;
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (hProcess == IntPtr.Zero) return "";


            var pbi = new ProcessBasicInformation();
            int returnLen = 0;
            int status = NtQueryInformationProcess(hProcess, 0, ref pbi, Marshal.SizeOf(pbi), ref returnLen);
            if (status != 0) { CloseHandle(hProcess); return ""; }


            IntPtr pebAddress = pbi.PebBaseAddress;
            int ptrSize = IntPtr.Size;
            IntPtr processParametersOffset = ptrSize == 8 ? (IntPtr)0x20 : (IntPtr)0x10;
            IntPtr ppPtr = IntPtr.Add(pebAddress, processParametersOffset.ToInt32());


            byte[] ppBytes = new byte[ptrSize];
            GCHandle hPP = GCHandle.Alloc(ppBytes, GCHandleType.Pinned);
            if (!ReadProcessMemory(hProcess, ppPtr, hPP.AddrOfPinnedObject(), (IntPtr)ptrSize, out _))
            { hPP.Free(); CloseHandle(hProcess); return ""; }
            long processParamsAddr = ptrSize == 8
                ? BitConverter.ToInt64(ppBytes, 0) : BitConverter.ToInt32(ppBytes, 0);
            hPP.Free();


            if (processParamsAddr == 0) { CloseHandle(hProcess); return ""; }


            IntPtr commandLineOffset = ptrSize == 8 ? (IntPtr)0x70 : (IntPtr)0x40;
            IntPtr clPtr = IntPtr.Add((IntPtr)processParamsAddr, commandLineOffset.ToInt32());
            int usSize = ptrSize == 8 ? 16 : 8;
            byte[] unicodeStrBytes = new byte[usSize];
            GCHandle h = GCHandle.Alloc(unicodeStrBytes, GCHandleType.Pinned);
            if (!ReadProcessMemory(hProcess, clPtr, h.AddrOfPinnedObject(), (IntPtr)usSize, out _))
            { h.Free(); CloseHandle(hProcess); return ""; }
            h.Free();


            int length = BitConverter.ToInt16(unicodeStrBytes, 0);
            int bufferOffset = ptrSize == 8 ? 8 : 4;
            long bufferAddr = ptrSize == 8
                ? BitConverter.ToInt64(unicodeStrBytes, bufferOffset)
                : BitConverter.ToInt32(unicodeStrBytes, bufferOffset);


            if (length <= 0 || bufferAddr == 0) { CloseHandle(hProcess); return ""; }


            byte[] cmdBytes = new byte[length];
            GCHandle h2 = GCHandle.Alloc(cmdBytes, GCHandleType.Pinned);
            if (!ReadProcessMemory(hProcess, (IntPtr)bufferAddr, h2.AddrOfPinnedObject(), (IntPtr)length, out _))
            { h2.Free(); CloseHandle(hProcess); return ""; }
            h2.Free();
            CloseHandle(hProcess);


            return System.Text.Encoding.Unicode.GetString(cmdBytes).TrimEnd('\0');
        }
        catch { }
        return "";
    }


    private string GetPlaceIdFromLogs()
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "Roblox", "logs");
            if (!Directory.Exists(logDir)) return "";


            var logFiles = new DirectoryInfo(logDir)
                .GetFiles("Player*.log")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(5);


            foreach (var logFile in logFiles)
            {
                try
                {
                    var content = File.ReadAllText(logFile.FullName);
                    var patterns = new[] {
                        @"placeid[""\s:=]+(\d{5,})", @"placeId[""\s:=]+(\d{5,})",
                        @"PlaceID[""\s:=]+(\d{5,})", @"place_id[""\s:=]+(\d{5,})"
                    };
                    foreach (var pattern in patterns)
                    {
                        var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
                        if (match.Success) return match.Groups[1].Value;
                    }
                }
                catch { }
            }
        }
        catch { }
        return "";
    }


    private async Task<(string gameName, string thumbUrl)> GetGameInfoAsync(string placeId)
    {
        try
        {
            var universeUrl = $"https://apis.roblox.com/universes/v1/places/{placeId}/universe";
            var uniJson = JsonDocument.Parse(await _http.GetStringAsync(universeUrl));
            var universeId = uniJson.RootElement.TryGetProperty("universeId", out var uid) ? uid.GetInt64().ToString() : "";
            if (string.IsNullOrEmpty(universeId)) return ("", "");


            var gameUrl = $"https://games.roblox.com/v1/games?universeIds={universeId}";
            var gameJson = JsonDocument.Parse(await _http.GetStringAsync(gameUrl));
            var gameName = "";
            if (gameJson.RootElement.TryGetProperty("data", out var gameData) && gameData.GetArrayLength() > 0)
                gameName = gameData[0].TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";


            var thumbUrl = "";
            var thumbUrlReq = $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&size=150x150&format=Png&isCircular=false";
            var thumbJson = JsonDocument.Parse(await _http.GetStringAsync(thumbUrlReq));
            if (thumbJson.RootElement.TryGetProperty("data", out var thumbData) && thumbData.GetArrayLength() > 0)
                thumbUrl = thumbData[0].TryGetProperty("imageUrl", out var urlProp) ? urlProp.GetString() ?? "" : "";


            return (gameName, thumbUrl);
        }
        catch { }
        return ("", "");
    }


    private string GetProcessPath(Process proc)
    {
        try { return proc.MainModule?.FileName ?? ""; } catch { return ""; }
    }


    public void KillRoblox()
    {
        try
        {
            var processes = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (var proc in processes)
            {
                try { proc.Kill(); } catch { }
            }
            System.Threading.Thread.Sleep(2000);
        }
        catch { }
    }


    public bool IsRunning()
    {
        try { return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0; }
        catch { return false; }
    }


    public string GetRobloxVersion()
    {
        try
        {
            string robloxPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (Directory.Exists(robloxPath))
            {
                var versionDirs = Directory.GetDirectories(robloxPath, "version-*");
                if (versionDirs.Length > 0)
                {
                    var latest = versionDirs[versionDirs.Length - 1];
                    return Path.GetFileName(latest);
                }
            }
        }
        catch { }
        return "Unknown";
    }


    public string GetRobloxPath()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string robloxPath = Path.Combine(localAppData, "Roblox", "Versions");
            if (Directory.Exists(robloxPath))
            {
                var exe = Path.Combine(robloxPath, "RobloxPlayerBeta.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }
        return "";
    }


    public string FindRobloxExe()
    {
        try
        {
            string saved = SettingsService.Instance.Get<string>("RobloxExePath") ?? "";
            if (string.IsNullOrEmpty(saved))
                saved = SettingsService.Instance.Get<string>("roblox_exe_path") ?? "";
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return saved;
        }
        catch { }


        string path = GetRobloxPath();
        if (!string.IsNullOrEmpty(path)) return path;


        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string versionsDir = Path.Combine(localAppData, "Roblox", "Versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir, "version-*").OrderByDescending(d => d))
                {
                    string exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        catch { }


        try
        {
            foreach (var proc in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                try { return proc.MainModule?.FileName ?? ""; } catch { }
            }
        }
        catch { }


        return "";
    }


    public bool LaunchRoblox()
    {
        string exe = FindRobloxExe();
        if (string.IsNullOrEmpty(exe)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
            return true;
        }
        catch { return false; }
    }


    public async Task<bool> WaitForRobloxAsync(int timeoutMs = 20000)
    {
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            if (IsRunning()) return true;
            await Task.Delay(200);
            elapsed += 200;
        }
        return IsRunning();
    }


    public List<string> GetInstalledVersions()
    {
        var versions = new List<string>();
        try
        {
            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (Directory.Exists(versionsDir))
            {
                foreach (var dir in Directory.GetDirectories(versionsDir, "version-*").OrderByDescending(d => d))
                {
                    string exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
                    if (File.Exists(exe))
                        versions.Add(Path.GetFileName(dir));
                }
            }
        }
        catch { }
        return versions;
    }


    public bool UninstallVersion(string versionFolder)
    {
        try
        {
            string versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", versionFolder);
            if (Directory.Exists(versionsDir))
            {
                Directory.Delete(versionsDir, true);
                return true;
            }
        }
        catch { }
        return false;
    }


    private void DetectAndNotify()
    {
        try
        {
            string version = GetRobloxVersion();
            string path = FindRobloxExe();
            string folder = GetRobloxFolder();
            _lastDetectedVersion = version;
            _lastDetectedPath = path;
            _lastDetectedFolder = folder;
            if (!string.IsNullOrEmpty(path))
            {
                SettingsService.Instance.Set("RobloxExePath", path);
                SettingsService.Instance.Set("roblox_exe_path", path);
            }
            if (!string.IsNullOrEmpty(version))
                SettingsService.Instance.Set("RobloxVersion", version);
            OnRobloxDetected?.Invoke(version, path, folder);
        }
        catch { }
    }


    public void PollRobloxStatus()
    {
        bool running = IsRunning();
        if (running && !_wasRunning)
        {
            _wasRunning = true;
            DetectAndNotify();
        }
        else if (!running && _wasRunning)
        {
            _wasRunning = false;
            OnRobloxClosed?.Invoke();
        }
        else if (running)
        {
            string currentPath = FindRobloxExe();
            if (!string.IsNullOrEmpty(currentPath) && currentPath != _lastDetectedPath)
                DetectAndNotify();
        }
    }


    private string GetRobloxFolder()
    {
        try
        {
            string exe = FindRobloxExe();
            if (!string.IsNullOrEmpty(exe)) return Path.GetDirectoryName(exe) ?? "";
        }
        catch { }
        return "";
    }
}
