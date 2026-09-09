using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;


namespace LeitostrapV7.Core;


public class MemoryOffsetless
{
    private static MemoryOffsetless? _instance;
    public static MemoryOffsetless Instance => _instance ??= new MemoryOffsetless();


    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);


    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);


    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesWritten);


    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out int lpNumberOfBytesRead);


    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);


    [DllImport("kernel32.dll")]
    private static extern bool EnumProcessModules(IntPtr hProcess, [Out] IntPtr[] lphModule, int cb, out int lpcbNeeded);


    [DllImport("kernel32.dll")]
    private static extern int GetModuleBaseNameW(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, int nSize);


    [DllImport("ntdll.dll")]
    private static extern int NtWriteVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, int size, out int bytesWritten);


    [DllImport("ntdll.dll")]
    private static extern int NtReadVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, int size, out int bytesRead);


    [DllImport("kernel32.dll")]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);


    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);


    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);


    [DllImport("kernel32.dll")]
    private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);


    [DllImport("kernel32.dll")]
    private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlsSnapCount;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }


    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;


    private bool _watchdogRunning;
    private Dictionary<string, string> _watchdogFlags = new();
    public Action<List<(string, string, string, long)>>? OnFlagsFixed;


    private const ulong FNV64Basis = 0xcbf29ce484222325;
    private const ulong FNV64Prime = 0x100000001b3;
    private const uint FNV32Basis = 0x811C9DC5;
    private const uint FNV32Prime = 0x01000193;
    private const ulong MinValidPtr = 0x10000;


    private const int OffValuePtr = 0xC0;
    private const int OffMapEnd = 0x00;
    private const int OffMapList = 0x10;
    private const int OffMapMask = 0x28;
    private const int OffEntryNext = 0x08;
    private const int OffEntryStr = 0x10;
    private const int OffEntryGetset = 0x30;
    private const int OffStrSize = 0x10;
    private const int OffStrAlloc = 0x18;


    private static readonly byte[][] SingletonPatterns = new byte[][]
    {
        new byte[] { 0x48, 0x83, 0xEC, 0x38, 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x4C, 0x8D, 0x05 },
        new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0xE8, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x8B, 0x0D },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC9, 0x74, 0x2E, 0x48, 0x83, 0xC1 },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x0F, 0x84, 0x2E, 0x2E, 0x2E, 0x2E },
        new byte[] { 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x0F, 0x84, 0x2E, 0x2E, 0x2E, 0x2E },
    };


    private static readonly (int disp, int next)[] PatternDispOffsets = { (3, 7), (7, 11), (4, 8), (2, 6), (10, 14) };


    private static readonly Dictionary<string, int> FlagPrefixes = new()
    {
        ["SDFString"] = 9, ["DFString"] = 8, ["SFString"] = 8, ["FString"] = 7,
        ["SDFInt"] = 6, ["DFInt"] = 5, ["SFInt"] = 5, ["FInt"] = 4,
        ["DFLog"] = 5, ["FLog"] = 4,
        ["SDFFlag"] = 7, ["DFFlag"] = 6, ["SFFlag"] = 6, ["FFlag"] = 5,
        ["SDFFloat"] = 8, ["DFFloat"] = 7, ["SFFloat"] = 7, ["FFloat"] = 6,
        ["SDFDouble"] = 9, ["DFDouble"] = 8, ["SFDouble"] = 8, ["FDouble"] = 7,
        ["DFVariable"] = 10, ["FVariable"] = 9,
        ["Flag"] = 4, ["Int"] = 3, ["Float"] = 5, ["Double"] = 6, ["String"] = 6, ["Log"] = 3, ["Variable"] = 8,
    };


    private IntPtr _handle;
    private int _pid;
    private long _modBase;
    private long _modSize;
    private long _singleton;
    private long _mapEnd;
    private long _mapList;
    private long _mapMask;
    private Dictionary<string, long> _hashCache = new();
    private Dictionary<string, long> _ptrCache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);


    private int FindProcess(string name)
    {
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
            {
                try { return proc.Id; }
                catch { }
            }
            return 0;
        }
        try
        {
            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (string.Equals(entry.szExeFile, name, StringComparison.OrdinalIgnoreCase))
                        return (int)entry.th32ProcessID;
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally { CloseHandle(snapshot); }
        return 0;
    }


    private bool GetModuleToolhelp32(string moduleName, out long baseAddr, out long baseSize)
    {
        baseAddr = 0;
        baseSize = 0;
        if (_pid == 0) return false;


        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)_pid);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return false;
        try
        {
            var entry = new MODULEENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32));
            if (Module32First(snapshot, ref entry))
            {
                do
                {
                    if (string.Equals(entry.szModule, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        baseAddr = (long)entry.modBaseAddr;
                        baseSize = entry.modBaseSize;
                        return true;
                    }
                }
                while (Module32Next(snapshot, ref entry));
            }
        }
        finally { CloseHandle(snapshot); }
        return false;
    }


    public bool Attach(int pid)
    {
        Detach();
        _pid = pid;
        foreach (uint access in new uint[] { 0x38, PROCESS_ALL_ACCESS })
        {
            _handle = OpenProcess(access, false, pid);
            if (_handle != IntPtr.Zero) break;
        }
        if (_handle == IntPtr.Zero) return false;


        if (GetModuleToolhelp32("RobloxPlayerBeta.exe", out long baseAddr, out long baseSize) ||
            GetModule("RobloxPlayerBeta.exe", out baseAddr, out baseSize))
        {
            _modBase = baseAddr;
            _modSize = baseSize;
            return true;
        }


        CloseHandle(_handle);
        _handle = IntPtr.Zero;
        return false;
    }


    public bool AttachCore()
    {
        int pid = FindProcess("RobloxPlayerBeta.exe");
        if (pid == 0) return false;
        return Attach(pid);
    }


    public void Detach()
    {
        _watchdogRunning = false;
        if (_handle != IntPtr.Zero)
            CloseHandle(_handle);
        _handle = IntPtr.Zero;
        _modBase = 0;
        _modSize = 0;
        _singleton = 0;
        _mapEnd = 0;
        _mapList = 0;
        _mapMask = 0;
        _hashCache.Clear();
        _ptrCache.Clear();
        _pid = 0;
    }


    private bool GetModule(string moduleName, out long baseAddr, out long baseSize)
    {
        baseAddr = 0;
        baseSize = 0;
        try
        {
            IntPtr[] modules = new IntPtr[1024];
            if (!EnumProcessModules(_handle, modules, modules.Length * IntPtr.Size, out int needed))
                return false;
            int count = needed / IntPtr.Size;
            for (int i = 0; i < count; i++)
            {
                StringBuilder sb = new StringBuilder(260);
                GetModuleBaseNameW(_handle, modules[i], sb, 260);
                if (string.Equals(sb.ToString(), moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    baseAddr = (long)modules[i];
                    var procs = Process.GetProcessById(_pid);
                    foreach (ProcessModule m in procs.Modules)
                    {
                        if (string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            baseSize = m.ModuleMemorySize;
                            return true;
                        }
                    }
                    return true;
                }
            }
        }
        catch { }
        return false;
    }


    private byte[] ReadMem(long address, int size)
    {
        if (_handle == IntPtr.Zero) return Array.Empty<byte>();
        try
        {
            byte[] buf = new byte[size];
            if (NtReadVirtualMemory(_handle, new IntPtr(address), buf, size, out int read) == 0 && read == size)
                return buf;
            if (ReadProcessMemory(_handle, new IntPtr(address), buf, size, out read) && read == size)
                return buf;
        }
        catch { }
        return Array.Empty<byte>();
    }


    private long ReadI64(long address)
    {
        if (_handle == IntPtr.Zero) return 0;
        try
        {
            byte[] buf = new byte[8];
            if (NtReadVirtualMemory(_handle, new IntPtr(address), buf, 8, out int read) == 0 && read == 8)
                return BitConverter.ToInt64(buf, 0);
            if (ReadProcessMemory(_handle, new IntPtr(address), buf, 8, out read) && read == 8)
                return BitConverter.ToInt64(buf, 0);
        }
        catch { }
        return 0;
    }


    public int ReadInt(long address)
    {
        if (_handle == IntPtr.Zero) return 0;
        try
        {
            byte[] buf = new byte[4];
            if (NtReadVirtualMemory(_handle, new IntPtr(address), buf, 4, out int read) == 0 && read == 4)
                return BitConverter.ToInt32(buf, 0);
            if (ReadProcessMemory(_handle, new IntPtr(address), buf, 4, out read) && read == 4)
                return BitConverter.ToInt32(buf, 0);
        }
        catch { }
        return 0;
    }


    private bool IsValidPtr(long ptr) => ptr >= (long)MinValidPtr && ptr <= 0x7FFFFFFFFFFF;


    private ulong Fnv1a64(string name)
    {
        if (_hashCache.TryGetValue(name, out long cached))
            return (ulong)cached;
        ulong h = FNV64Basis;
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            h ^= b;
            h = (h * FNV64Prime) & 0xFFFFFFFFFFFFFFFF;
        }
        _hashCache[name] = (long)h;
        return h;
    }


    private uint Fnv1a32(string name)
    {
        uint h = FNV32Basis;
        foreach (char ch in name)
        {
            h ^= (uint)ch;
            h = (h * FNV32Prime) & 0xFFFFFFFF;
        }
        return h;
    }


    public long FindSingleton()
    {
        if (_modBase == 0 || _handle == IntPtr.Zero) return 0;


        foreach (byte[] pat in SingletonPatterns)
        {
            List<long> addrs = PatternScan(pat);
            if (addrs.Count == 0) continue;


            for (int pi = 0; pi < addrs.Count; pi++)
            {
                long addr = addrs[pi];
                int patIdx = Array.IndexOf(SingletonPatterns, pat);
                if (patIdx < 0 || patIdx >= PatternDispOffsets.Length) continue;
                var (dispOff, nextOff) = PatternDispOffsets[patIdx];


                try
                {
                    byte[] raw = ReadMem(addr, 32);
                    if (raw.Length < nextOff + 4) continue;


                    int disp = BitConverter.ToInt32(raw, dispOff);
                    long ptrAddr = addr + nextOff + disp;
                    if (ptrAddr < (long)MinValidPtr) continue;


                    long val = ReadI64(ptrAddr);
                    if (val > (long)MinValidPtr)
                    {
                        _singleton = val + 8;
                        return _singleton;
                    }
                }
                catch { }
            }
        }
        return 0;
    }


    private List<long> PatternScan(byte[] pattern)
    {
        List<long> results = new();
        if (_modBase == 0 || _modSize == 0 || _handle == IntPtr.Zero) return results;


        int patternLen = pattern.Length;
        int scanSize = (int)Math.Min(_modSize, 0x2000000);
        int chunkSize = 65536;


        for (long offset = 0; offset < scanSize; offset += chunkSize - patternLen + 1)
        {
            int toRead = (int)Math.Min(chunkSize, scanSize - offset);
            if (toRead <= 0) break;


            byte[] readBuf = new byte[toRead];
            if (!ReadProcessMemory(_handle, new IntPtr(_modBase + offset), readBuf, toRead, out int bytesRead) || bytesRead == 0)
                continue;


            for (int i = 0; i <= bytesRead - patternLen; i++)
            {
                bool match = true;
                for (int j = 0; j < patternLen; j++)
                {
                    if (pattern[j] != 0x2E && pattern[j] != readBuf[i + j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    results.Add(_modBase + offset + i);
            }
        }
        return results;
    }


    private bool GetMapHeader()
    {
        if (_singleton != 0 && _mapMask != 0 && _mapList != 0) return true;
        if (_singleton == 0) FindSingleton();
        if (_singleton == 0) return false;


        byte[] raw = ReadMem(_singleton, 56);
        if (raw.Length < 56) return false;


        long end = BitConverter.ToInt64(raw, OffMapEnd);
        long lst = BitConverter.ToInt64(raw, OffMapList);
        long mask = BitConverter.ToInt64(raw, OffMapMask);


        if (mask == 0 || lst == 0 || !IsValidPtr(lst))
        {
            int[][] altOffsets = { new[] { 0x00, 0x10, 0x28 }, new[] { 0x08, 0x18, 0x30 }, new[] { 0x00, 0x18, 0x38 } };
            bool found = false;
            foreach (var off in altOffsets)
            {
                try
                {
                    long me = BitConverter.ToInt64(raw, off[0]);
                    long ml = BitConverter.ToInt64(raw, off[1]);
                    long mm = BitConverter.ToInt64(raw, off[2]);
                    if (mm != 0 && ml > (long)MinValidPtr)
                    {
                        end = me; lst = ml; mask = mm;
                        found = true;
                        break;
                    }
                }
                catch { }
            }
            if (!found) return false;
        }


        _mapEnd = end;
        _mapList = lst;
        _mapMask = mask;
        return true;
    }


    private (byte[] nameBytes, int length) ReadNodeName(byte[] data)
    {
        int sz = BitConverter.ToInt32(data, OffEntryStr + OffStrSize);
        if (sz <= 0 || sz > 256) return (Array.Empty<byte>(), 0);


        int alloc = BitConverter.ToInt32(data, OffEntryStr + OffStrAlloc);
        if (alloc > 15)
        {
            long p = BitConverter.ToInt64(data, OffEntryStr);
            if (!IsValidPtr(p)) return (Array.Empty<byte>(), 0);
            byte[] nb = ReadMem(p, sz);
            if (nb.Length == 0) return (Array.Empty<byte>(), 0);
            return (nb, sz);
        }
        byte[] result = new byte[sz];
        Array.Copy(data, OffEntryStr, result, 0, sz);
        return (result, sz);
    }


    private void CachePtr(string name, long ptr)
    {
        if (ptr == 0) return;
        _ptrCache[name] = ptr;
        if (_ptrCache.Count > 4096)
        {
            var first = new List<string>(_ptrCache.Keys)[0];
            _ptrCache.Remove(first);
        }
    }


    private long GetCachedPtr(string name)
    {
        if (_ptrCache.TryGetValue(name, out long ptr) && ptr != 0) return ptr;
        return 0;
    }


    public long FindFlag(string name)
    {
        long cached = GetCachedPtr(name);
        if (cached != 0) return cached;
        if (!GetMapHeader()) return 0;


        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        ulong hPrimary = Fnv1a64(name) & (ulong)_mapMask;
        long res = ScanBucket((long)hPrimary, nameBytes, name);
        if (res != 0) return res;


        uint hAlt = Fnv1a32(name) & (uint)_mapMask;
        if ((long)hAlt != (long)hPrimary)
        {
            res = ScanBucket((long)hAlt, nameBytes, name);
            if (res != 0) return res;
        }
        return 0;
    }


    private long ScanBucket(long bucketIndex, byte[] nameBytes, string nameStr)
    {
        long bb = _mapList + (bucketIndex * 16);
        byte[] bd = ReadMem(bb, 16);
        if (bd.Length < 16) return 0;


        long nc = BitConverter.ToInt64(bd, 8);
        if (!IsValidPtr(nc) || nc == _mapEnd) return 0;


        var visited = new HashSet<long>();
        for (int it = 0; it < 128; it++)
        {
            if (visited.Contains(nc)) break;
            visited.Add(nc);


            byte[] ed = ReadMem(nc, 64);
            if (ed.Length < 64) break;


            long fw = BitConverter.ToInt64(ed, OffEntryNext);
            var (enb, el) = ReadNodeName(ed);


            if (el == nameBytes.Length && enb.Length == nameBytes.Length)
            {
                bool match = true;
                for (int i = 0; i < nameBytes.Length; i++)
                {
                    if (enb[i] != nameBytes[i]) { match = false; break; }
                }
                if (match)
                {
                    long gs = BitConverter.ToInt64(ed, OffEntryGetset);
                    if (IsValidPtr(gs))
                    {
                        CachePtr(nameStr, gs);
                        return gs;
                    }
                }
            }


            if (!IsValidPtr(fw) || fw == nc) break;
            nc = fw;
        }
        return 0;
    }


    private bool WriteMemory(long addr, byte[] data, int maxRetries = 3)
    {
        if (data == null || data.Length == 0) return false;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                int bytesWritten;
                int status = NtWriteVirtualMemory(_handle, new IntPtr(addr), data, data.Length, out bytesWritten);
                if (status == 0) return true;


                uint oldProtect;
                if (VirtualProtectEx(_handle, new IntPtr(addr), (UIntPtr)(ulong)data.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
                {
                    status = NtWriteVirtualMemory(_handle, new IntPtr(addr), data, data.Length, out bytesWritten);
                    uint dummy;
                    VirtualProtectEx(_handle, new IntPtr(addr), (UIntPtr)(ulong)data.Length, oldProtect, out dummy);
                    if (status == 0) return true;
                }


                if (WriteProcessMemory(_handle, new IntPtr(addr), data, data.Length, out bytesWritten) && bytesWritten == data.Length)
                    return true;
            }
            catch { }
            if (attempt < maxRetries - 1)
                Thread.Sleep(50 * (attempt + 1));
        }
        return false;
    }


    private static string CleanFlagPrefix(string name)
    {
        string clean = name;
        foreach (var kv in FlagPrefixes.OrderByDescending(p => p.Key.Length))
        {
            if (name.StartsWith(kv.Key, StringComparison.Ordinal))
            {
                clean = name.Substring(kv.Key.Length);
                break;
            }
        }
        return clean;
    }


    private static (string clean, string type, object pval) ParseFlag(string key, string val)
    {
        string valStr = val?.Trim() ?? "";
        string clean = key;


        foreach (var kv in FlagPrefixes.OrderByDescending(p => p.Key.Length))
        {
            if (key.StartsWith(kv.Key, StringComparison.Ordinal))
            {
                clean = key.Substring(kv.Key.Length);
                break;
            }
        }


        if (key.StartsWith("FFlag") || key.StartsWith("DFFlag") || key.StartsWith("SFFlag") || key.StartsWith("SDFFlag"))
            return (clean, "bool", valStr.ToLowerInvariant() is "true" or "1" or "yes");


        if (key.StartsWith("FInt") || key.StartsWith("DFInt") || key.StartsWith("FLog") || key.StartsWith("DFLog") ||
            key.StartsWith("SFInt") || key.StartsWith("SDFInt"))
        {
            if (int.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out int iv))
                return (clean, "int", iv);
            return (clean, "int", 0);
        }


        if (key.StartsWith("FString") || key.StartsWith("DFString") || key.StartsWith("SFString") || key.StartsWith("SDFString"))
            return (clean, "string", valStr);


        if (key.StartsWith("FFloat") || key.StartsWith("DFFloat") || key.StartsWith("SFFloat") || key.StartsWith("SDFFloat") ||
            key.StartsWith("FDouble") || key.StartsWith("DFDouble") || key.StartsWith("SFDouble") || key.StartsWith("SDFDouble"))
        {
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv))
                return (clean, "float", fv);
            return (clean, "float", 0.0f);
        }


        if (bool.TryParse(valStr, out bool bv)) return (clean, "bool", bv);
        if (int.TryParse(valStr, out int intv)) return (clean, "int", intv);
        if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float flv))
            return (clean, "float", flv);


        return (clean, "string", valStr);
    }


    private bool WriteIntValue(string name, int val)
    {
        long addr = FindFlag(name);
        if (addr == 0) return false;
        try
        {
            byte[] fieldData = ReadMem(addr, 0xD0);
            if (fieldData.Length < 0xD0) return false;
            long vp = BitConverter.ToInt64(fieldData, OffValuePtr);
            if (vp == 0) return false;
            return WriteMemory(vp, BitConverter.GetBytes(val));
        }
        catch { return false; }
    }


    private bool WriteStringValue(string name, string val)
    {
        long addr = FindFlag(name);
        if (addr == 0) return false;
        try
        {
            byte[] fieldData = ReadMem(addr, 0xD0);
            if (fieldData.Length < 0xD0) return false;
            long valueAddr = BitConverter.ToInt64(fieldData, OffValuePtr);
            if (valueAddr == 0) return false;
            int capacity = BitConverter.ToInt32(fieldData, OffValuePtr + 0x10);
            byte[] encoded = Encoding.UTF8.GetBytes(val);
            int n = encoded.Length;
            const int SSO_CAP = 15;


            if (n <= SSO_CAP)
            {
                byte[] obj = new byte[32];
                Array.Copy(encoded, 0, obj, 0, n);
                BitConverter.GetBytes(n).CopyTo(obj, 16);
                BitConverter.GetBytes(SSO_CAP).CopyTo(obj, 24);
                return WriteMemory(valueAddr, obj);
            }


            if (n > capacity) return false;
            long bufPtr = ReadI64(valueAddr);
            if (!IsValidPtr(bufPtr)) return false;
            byte[] nullTerm = new byte[n + 1];
            Array.Copy(encoded, nullTerm, n);
            if (!WriteMemory(bufPtr, nullTerm)) return false;
            return WriteMemory(valueAddr + 8, BitConverter.GetBytes((long)n));
        }
        catch { return false; }
    }


    private bool WriteFloatValue(string name, float val)
    {
        long addr = FindFlag(name);
        if (addr == 0) return false;
        try
        {
            byte[] fieldData = ReadMem(addr, 0xD0);
            if (fieldData.Length < 0xD0) return false;
            long vp = BitConverter.ToInt64(fieldData, OffValuePtr);
            if (vp == 0) return false;
            return WriteMemory(vp, BitConverter.GetBytes(val));
        }
        catch { return false; }
    }


    private bool WriteBoolValue(string name, bool val)
    {
        long addr = FindFlag(name);
        if (addr == 0) return false;
        try
        {
            byte[] fieldData = ReadMem(addr, 0xD0);
            if (fieldData.Length < 0xD0) return false;
            long vp = BitConverter.ToInt64(fieldData, OffValuePtr);
            if (vp == 0) return false;
            return WriteMemory(vp, new byte[] { (byte)(val ? 1 : 0) });
        }
        catch { return false; }
    }


    private bool WriteFlag(string key, string val)
    {
        try
        {
            var (clean, ftype, pval) = ParseFlag(key, val);
            switch (ftype)
            {
                case "bool": return WriteBoolValue(clean, (bool)pval);
                case "int": return WriteIntValue(clean, (int)pval);
                case "float": return WriteFloatValue(clean, Convert.ToSingle(pval));
                default: return WriteStringValue(clean, pval?.ToString() ?? "");
            }
        }
        catch { return false; }
    }


    public (bool ok, string msg, int written, int total) InjectAll(Dictionary<string, string> flags)
    {
        if (_handle == IntPtr.Zero)
        {
            if (!AttachCore())
                return (false, "Not attached to process", 0, 0);
        }
        if (flags == null || flags.Count == 0)
            return (false, "No flags to inject", 0, 0);


        if (_singleton == 0)
        {
            FindSingleton();
            if (_singleton == 0)
                return (false, "Could not find singleton", 0, flags.Count);
        }


        int total = flags.Count;
        int written = 0;


        foreach (var kv in flags)
        {
            try
            {
                if (WriteFlag(kv.Key, kv.Value))
                    written++;
            }
            catch { }
        }


        if (written > 0)
        {
            StartWatchdog(flags);
            return (true, $"Injected {written}/{total} flags", written, total);
        }
        return (false, $"Failed: 0/{total} flags injected", 0, total);
    }


    public (bool ok, string msg) InjectWithRetry(Dictionary<string, string> flags, int maxAttempts = 15, double totalTimeout = 30.0)
    {
        var startTime = DateTime.UtcNow;
        int finalWritten = 0;


        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 500 : 1500);
            try
            {
                if (!AttachCore()) continue;


                if (_singleton == 0)
                {
                    FindSingleton();
                    if (_singleton == 0) continue;
                }


                var result = InjectAll(flags);
                if (result.ok && result.written > 0)
                {
                    finalWritten = result.written;
                    if (result.written == flags.Count)
                        break;
                }
            }
            catch { }


            if ((DateTime.UtcNow - startTime).TotalSeconds > totalTimeout) break;
        }


        if (finalWritten > 0)
            return (true, $"Injected {finalWritten}/{flags.Count} flags");
        return (false, $"Failed after {maxAttempts} attempts");
    }


    public void StartWatchdog(Dictionary<string, string> flagsDict)
    {
        if (_watchdogRunning) return;
        _watchdogRunning = true;
        _watchdogFlags = new Dictionary<string, string>(flagsDict);
        new Thread(WatchdogLoop) { IsBackground = true, Name = "LeitostrapOffsetlessWatchdog" }.Start();
    }


    public void StopWatchdog() { _watchdogRunning = false; }


    private void WatchdogLoop()
    {
        int failCount = 0;
        int cycle = 0;
        while (_watchdogRunning)
        {
            Thread.Sleep(500);
            try
            {
                if (_watchdogFlags.Count == 0) continue;
                if (_handle == IntPtr.Zero || !AttachCore())
                {
                    failCount++;
                    if (failCount > 3)
                    {
                        _handle = IntPtr.Zero;
                        _modBase = 0;
                        _singleton = 0;
                        AttachCore();
                    }
                    continue;
                }
                failCount = 0;
                var reverted = new List<(string, string, string, long)>();


                foreach (var kv in _watchdogFlags)
                {
                    try
                    {
                        long addr = FindFlag(kv.Key);
                        if (addr == 0) continue;
                        var parsed = ParseFlag(kv.Key, kv.Value);
                        if (parsed.type != "int") continue;
                        int expected = (int)parsed.pval;
                        int current = ReadInt(addr + OffValuePtr);
                        if (current != expected)
                        {
                            if (WriteMemory(addr + OffValuePtr, BitConverter.GetBytes(expected)))
                                reverted.Add((kv.Key, parsed.clean, "int", expected));
                        }
                    }
                    catch { }
                }


                cycle++;
                if (cycle >= 10)
                {
                    cycle = 0;
                    try
                    {
                        foreach (var kv in _watchdogFlags)
                        {
                            try { WriteFlag(kv.Key, kv.Value); } catch { }
                        }
                    }
                    catch { }
                }


                if (reverted.Count > 0)
                    OnFlagsFixed?.Invoke(reverted);
            }
            catch { }
        }
    }
}
