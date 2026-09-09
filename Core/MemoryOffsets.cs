using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;


namespace LeitostrapV7.Core;


public class MemoryOffsets
{
    private static MemoryOffsets? _instance;
    public static MemoryOffsets Instance => _instance ??= new MemoryOffsets();


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
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);


    [DllImport("kernel32.dll")]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);


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
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;


    private const ulong FNV64Basis = 0xcbf29ce484222325;
    private const ulong FNV64Prime = 0x100000001b3;
    private const uint FNV32Basis = 0x811C9DC5;
    private const uint FNV32Prime = 0x01000193;
    private const ulong MinValidPtr = 0x10000;
    private const int MaxChain = 1000;


    private const int OffValuePtr = 0xC0;
    private const int OffMapEnd = 0x00;
    private const int OffMapList = 0x10;
    private const int OffMapMask = 0x28;
    private const int OffEntryNext = 0x08;
    private const int OffEntryStr = 0x10;
    private const int OffEntryGetset = 0x30;
    private const int OffStrSize = 0x10;
    private const int OffStrAlloc = 0x18;


    private static readonly int[] FflagListOffsets = { 0x84b4b28, 0x805f198, 0x7723F28, 0x7C9FB78, 0x7CA5B78, 0x7C1E3D8, 0x843d0a8 };


    private static readonly byte[][] SingletonPatterns = new byte[][]
    {
        new byte[] { 0x48, 0x83, 0xEC, 0x38, 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x4C, 0x8D, 0x05 },
        new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0xE8, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x8B, 0x0D },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC9, 0x74, 0x2E, 0x48, 0x83, 0xC1 },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x0F, 0x84, 0x2E, 0x2E, 0x2E, 0x2E },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x74, 0x2E, 0x48, 0x8B, 0x00 },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x89, 0x5C, 0x24, 0x20 },
        new byte[] { 0x48, 0x8B, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x0F, 0x85, 0x2E, 0x2E, 0x2E, 0x2E },
        new byte[] { 0x48, 0x8D, 0x0D, 0x2E, 0x2E, 0x2E, 0x2E, 0xE8, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x8B, 0x0D },
        new byte[] { 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x0F, 0x84, 0x2E, 0x2E, 0x2E, 0x2E },
        new byte[] { 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x74, 0x2E, 0x48, 0x8B },
        new byte[] { 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0, 0x75, 0x2E, 0x48, 0x8B },
        new byte[] { 0x48, 0x83, 0xEC, 0x48, 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0 },
        new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x48, 0x8B, 0x05, 0x2E, 0x2E, 0x2E, 0x2E, 0x48, 0x85, 0xC0 },
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
    private Dictionary<string, long> _offsets = new();
    private Dictionary<string, long> _hashCache = new();
    private Dictionary<string, long> _ptrCache = new();
    private bool _watchdogRunning;
    private Dictionary<string, string> _watchdogFlags = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    public Action<List<(string, string, string, long)>>? OnFlagsFixed;


    public IntPtr Handle => _handle;
    public long ModBase => _modBase;
    public long Singleton => _singleton;
    public bool IsAttached => _handle != IntPtr.Zero && _modBase != 0;


    public void SetOffsets(Dictionary<string, long> offsets) { _offsets = offsets ?? new(); }


    public static string CleanFlagPrefix(string name)
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


    private bool IsValidPtr(long ptr) => ptr >= (long)MinValidPtr && ptr <= 0x7FFFFFFFFFFF;


    private void CachePtr(string name, long ptr)
    {
        if (ptr == 0) return;
        _ptrCache[name] = ptr;
        if (_ptrCache.Count > 4096)
        {
            var first = _ptrCache.Keys.First();
            _ptrCache.Remove(first);
        }
    }


    private long GetCachedPtr(string name)
    {
        if (_ptrCache.TryGetValue(name, out long ptr) && ptr != 0)
            return ptr;
        return 0;
    }


    public ulong Fnv1a64(string name)
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


    public uint Fnv1a32(string name)
    {
        uint h = FNV32Basis;
        foreach (char ch in name)
        {
            h ^= (uint)ch;
            h = (h * FNV32Prime) & 0xFFFFFFFF;
        }
        return h;
    }


    private int ExpectedInt32(string val)
    {
        string lower = val.Trim().ToLowerInvariant();
        if (lower is "true" or "1" or "yes") return 1;
        if (lower is "false" or "0" or "no") return 0;
        if (double.TryParse(lower, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
            return Math.Max(int.MinValue, Math.Min(int.MaxValue, (int)d));
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


    public bool AttachCore()
    {
        int pid = FindProcess("RobloxPlayerBeta.exe");
        if (pid == 0) return false;


        if (_pid != pid)
        {
            DetachCore();
            _pid = pid;


            foreach (uint access in new uint[] { 0x38, PROCESS_ALL_ACCESS })
            {
                _handle = OpenProcess(access, false, pid);
                if (_handle != IntPtr.Zero) break;
            }
            if (_handle == IntPtr.Zero) return false;


            for (int i = 0; i < 10; i++)
            {


                if (GetModuleToolhelp32("RobloxPlayerBeta.exe", out long baseAddr, out long baseSize) ||
                    GetModule("RobloxPlayerBeta.exe", out baseAddr, out baseSize))
                {
                    _modBase = baseAddr;
                    _modSize = baseSize;
                    break;
                }
                Thread.Sleep(150);
            }


            if (_modBase != 0 && _singleton == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    FindSingleton();
                    if (_singleton != 0) break;
                    Thread.Sleep(200);
                }
            }
        }


        if (_handle != IntPtr.Zero && _singleton == 0 && _modBase != 0)
            FindSingleton();


        return _handle != IntPtr.Zero;
    }


    public void DetachCore()
    {
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


    private long PatternScanModule(byte[] pattern)
    {
        if (_handle == IntPtr.Zero || _modBase == 0 || _modSize == 0) return 0;


        int patternLen = pattern.Length;
        int scanSize = (int)Math.Min(_modSize, 0x2000000);
        int chunkSize = 65536;
        byte[] buffer = new byte[chunkSize];


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
                    return _modBase + offset + i;
            }
        }
        return 0;
    }


    public long SingletonFromPattern()
    {
        if (_modBase == 0 || _handle == IntPtr.Zero) return 0;


        foreach (var pattern in SingletonPatterns)
        {
            try
            {
                long addr = PatternScanModule(pattern);
                if (addr == 0) continue;


                foreach (var (dispOff, nextOff) in PatternDispOffsets)
                {
                    try
                    {
                        byte[] dispBuf = ReadMem(addr + dispOff, 4);
                        if (dispBuf.Length < 4) continue;
                        int disp = BitConverter.ToInt32(dispBuf, 0);
                        long ptrAddr = addr + nextOff + disp;
                        if (ptrAddr < (long)MinValidPtr) continue;


                        long val = ReadI64(ptrAddr);
                        if (val > (long)MinValidPtr)
                        {
                            _singleton = val + 8;
                            return _singleton;
                        }
                    }
                    catch { continue; }
                }
            }
            catch { continue; }
        }
        return 0;
    }


    public long SingletonFromOffsets()
    {
        if (_modBase == 0 || _handle == IntPtr.Zero) return 0;


        foreach (int offset in FflagListOffsets)
        {
            try
            {
                long v = ReadI64(_modBase + offset);
                if (v > (long)MinValidPtr)
                {
                    long mapBase = v + 8;
                    byte[] test = ReadMem(mapBase, 8);
                    if (test.Length == 8)
                    {
                        _singleton = mapBase;
                        return mapBase;
                    }
                }
            }
            catch { }
        }
        return 0;
    }


    public void FindSingleton()
    {
        if (_modBase == 0 || _handle == IntPtr.Zero) return;


        long s = SingletonFromPattern();
        if (s != 0) return;


        SingletonFromOffsets();
    }


    public bool GetMapHeader()
    {
        if (_singleton != 0 && _mapMask != 0 && _mapList != 0) return true;


        long s = _singleton;
        if (s == 0)
        {
            FindSingleton();
            s = _singleton;
        }
        if (s == 0) return false;


        byte[] raw = ReadMem(s, 56);
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
                        end = me;
                        lst = ml;
                        mask = mm;
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


    public long FindFlag(string name)
    {
        long cached = GetCachedPtr(name);
        if (cached != 0) return cached;


        if (!GetMapHeader()) return 0;


        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        ulong hPrimary = Fnv1a64(name) & (ulong)_mapMask;


        long res = ScanBucket((long)hPrimary, nameBytes);
        if (res != 0) return res;


        uint hAlt = Fnv1a32(name) & (uint)_mapMask;
        if ((long)hAlt != (long)hPrimary)
        {
            res = ScanBucket((long)hAlt, nameBytes);
            if (res != 0) return res;
        }
        return 0;
    }


    private long ScanBucket(long bucketIndex, byte[] nameBytes)
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
                        CachePtr(Encoding.UTF8.GetString(nameBytes), gs);
                        return gs;
                    }
                }
            }


            if (!IsValidPtr(fw) || fw == nc) break;
            nc = fw;
        }
        return 0;
    }


    public bool WriteMemory(long addr, byte[] data, int maxRetries = 3)
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


    private bool WriteViaOffsetRaw(long address, int value, int maxRetries = 3)
    {
        return WriteMemory(address, BitConverter.GetBytes(value), maxRetries);
    }


    public bool WriteIntValue(string name, int val)
    {
        long addr = FindFlag(name);
        if (addr == 0) return false;
        try
        {
            byte[] fieldData = ReadMem(addr, 0xD0);
            if (fieldData.Length < 0xD0) return false;
            long vp = BitConverter.ToInt64(fieldData, OffValuePtr);
            if (vp == 0) return false;
            int clamped = Math.Max(int.MinValue, Math.Min(int.MaxValue, val));
            return WriteMemory(vp, BitConverter.GetBytes(clamped));
        }
        catch { return false; }
    }


    public bool WriteStringValue(string name, string val)
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


    public bool WriteFloatValue(string name, float val)
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


    public bool WriteBoolValue(string name, bool val)
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


    public (string cleanName, string type, object pval) ParseFlag(string key, string val)
    {
        string valStr = val?.Trim() ?? "";
        string clean = key;
        string ftype = "string";


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


        if (bool.TryParse(valStr, out bool bv))
            return (clean, "bool", bv);
        if (int.TryParse(valStr, out int intv))
            return (clean, "int", intv);
        if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float flv))
            return (clean, "float", flv);


        return (clean, "string", valStr);
    }


    public bool WriteHashmap(string key, string name, string ftype, object pval)
    {
        string[] tryNames = name != key ? new[] { key, name } : new[] { key };
        foreach (string tn in tryNames)
        {
            try
            {
                switch (ftype)
                {
                    case "bool":
                        if (WriteBoolValue(tn, (bool)pval)) return true;
                        break;
                    case "int":
                        if (WriteIntValue(tn, (int)pval)) return true;
                        break;
                    case "float":
                        if (WriteFloatValue(tn, Convert.ToSingle(pval))) return true;
                        break;
                    default:
                        if (WriteStringValue(tn, pval?.ToString() ?? "")) return true;
                        break;
                }
            }
            catch { }
        }
        return false;
    }


    public bool WriteViaOffset(string key, string name, string ftype, object pval, int maxRetries = 3)
    {
        string lookupKey = _offsets.ContainsKey(key) ? key : name;
        if (!_offsets.TryGetValue(lookupKey, out long offsetVal) || _modBase == 0) return false;


        long addr = _modBase + offsetVal;
        byte[] data;
        switch (ftype)
        {
            case "bool":
                data = BitConverter.GetBytes(((bool)pval) ? 1 : 0);
                break;
            case "int":
                data = BitConverter.GetBytes(Math.Max(int.MinValue, Math.Min(int.MaxValue, Convert.ToInt32(pval))));
                break;
            case "float":
                data = BitConverter.GetBytes(Convert.ToSingle(pval));
                break;
            default:
                return false;
        }


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


    public bool WriteFlag(string key, string val, bool preferOffsetless = false)
    {
        try
        {
            var (clean, ftype, pval) = ParseFlag(key, val);
            if (preferOffsetless)
            {
                if (WriteHashmap(key, clean, ftype, pval)) return true;
                if (WriteViaOffset(key, clean, ftype, pval)) return true;
                return false;
            }
            if (WriteViaOffset(key, clean, ftype, pval)) return true;
            if (WriteHashmap(key, clean, ftype, pval)) return true;
            return false;
        }
        catch { return false; }
    }


    public bool WriteFlagSafe(string key, string val, bool preferOffsetless = false)
    {
        try { return WriteFlag(key, val, preferOffsetless); }
        catch
        {
            try { return WriteFlag(key, val, !preferOffsetless); }
            catch { return false; }
        }
    }


    public (int ok, int fail) InjectFlags(Dictionary<string, string> flagsDict)
    {
        if (_handle == IntPtr.Zero || _offsets.Count == 0 || _modBase == 0)
            return (0, 0);


        int ok = 0, fail = 0;
        foreach (var kv in flagsDict)
        {
            string clean = CleanFlagPrefix(kv.Key);
            string offsetKey = _offsets.ContainsKey(clean) ? clean : kv.Key;
            bool written = false;


            if (_offsets.TryGetValue(offsetKey, out long offsetVal))
            {
                long address = _modBase + offsetVal;
                string valStr = kv.Value?.Trim() ?? "";
                if (valStr.ToLowerInvariant() is "true" or "1" or "yes")
                {
                    written = WriteViaOffsetRaw(address, 1);
                }
                else if (valStr.ToLowerInvariant() is "false" or "0" or "no")
                {
                    written = WriteViaOffsetRaw(address, 0);
                }
                else if (int.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out int intVal))
                {
                    written = WriteViaOffsetRaw(address, intVal);
                }
                else if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                {
                    byte[] fbytes = BitConverter.GetBytes(floatVal);
                    int rawInt = BitConverter.ToInt32(fbytes, 0);
                    written = WriteViaOffsetRaw(address, rawInt);
                }
            }


            if (written) ok++; else fail++;
        }
        return (ok, fail);
    }


    public (bool ok, string msg) MemoryInject(
        Dictionary<string, string> flags,
        bool preferOffsetless = true,
        Action<int, int>? progressCallback = null,
        double totalTimeout = 30.0)
    {
        if (!_lock.Wait(3000))
            return (false, "Injection already in progress");


        try
        {
            if (!AttachCore())
                return (false, "Roblox is not running");
            if (_handle == IntPtr.Zero)
                return (false, "Invalid process handle");
            if (flags == null || flags.Count == 0)
                return (false, "No flags to inject");


            var items = flags.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
            if (items.Count == 0)
                return (false, "No flags with values to apply");


            int total = items.Count;
            DateTime startTime = DateTime.UtcNow;


            if (_offsets.Count > 0 && _modBase != 0)
            {
                var flagDict = items.ToDictionary(kv => kv.Key, kv => kv.Value);
                var (ok, fail) = InjectFlags(flagDict);


                if (ok > 0 && fail > 0)
                {
                    foreach (var kv in items)
                    {
                        if ((DateTime.UtcNow - startTime).TotalSeconds > totalTimeout) break;
                        try
                        {
                            string clean = CleanFlagPrefix(kv.Key);
                            if (_offsets.ContainsKey(clean) || _offsets.ContainsKey(kv.Key))
                            {
                                string lookupKey = _offsets.ContainsKey(clean) ? clean : kv.Key;
                                long address = _modBase + _offsets[lookupKey];
                                var parsed = ParseFlag(kv.Key, kv.Value);
                                if (parsed.type == "int")
                                {
                                    int expected = (int)parsed.pval;
                                    int current = ReadInt(address);
                                    if (current == expected) continue;
                                }
                            }
                            if (WriteFlagSafe(kv.Key, kv.Value, preferOffsetless: true))
                            {
                                ok++;
                                fail--;
                            }
                        }
                        catch { }
                    }
                }


                if (ok > 0)
                {
                    StartWatchdog(flags);
                    return (true, $"Injected {ok}/{total} flags ({fail} failed)");
                }
                return (false, $"Failed: 0/{total} flags injected. Roblox may need to be restarted.");
            }


            int okCount = 0, failCount = 0;
            foreach (var kv in items)
            {
                try
                {
                    bool written = WriteFlagSafe(kv.Key, kv.Value, preferOffsetless);
                    if (written) okCount++; else failCount++;
                }
                catch { failCount++; }


                progressCallback?.Invoke(okCount + failCount, total);


                if ((DateTime.UtcNow - startTime).TotalSeconds > totalTimeout) break;
            }


            if (okCount > 0)
            {
                StartWatchdog(flags);
                return (true, $"Injected {okCount}/{total} flags ({failCount} failed)");
            }
            return (false, $"Failed: 0/{total} flags injected. Roblox may need to be restarted.");
        }
        catch (Exception ex)
        {
            return (false, $"Injection error: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }


    public void StartWatchdog(Dictionary<string, string> flagsDict)
    {
        if (_watchdogRunning) return;
        _watchdogRunning = true;
        _watchdogFlags = new Dictionary<string, string>(flagsDict);
        new Thread(_watchdog_loop) { IsBackground = true }.Start();
    }


    public void StopWatchdog() { _watchdogRunning = false; }


    private void _watchdog_loop()
    {
        ConsoleService.Instance.Log($"Watchdog: started, {_watchdogFlags.Count} flags, {_offsets.Count} offsets, handle={_handle != IntPtr.Zero}, modBase=0x{_modBase:X}", "Info");


        while (_watchdogRunning)
        {
            Thread.Sleep(2000);


            try
            {
                if (_watchdogFlags.Count == 0) continue;
                if (_handle == IntPtr.Zero || _modBase == 0) continue;


                int reapplied = 0;
                int skippedNoOffset = 0;
                int skippedNoValue = 0;


                foreach (var kv in _watchdogFlags)
                {
                    string clean = CleanFlagPrefix(kv.Key);
                    if (!_offsets.TryGetValue(clean, out long offsetVal) && !_offsets.TryGetValue(kv.Key, out offsetVal))
                    {
                        skippedNoOffset++;
                        continue;
                    }


                    long address = _modBase + offsetVal;
                    int current = ReadInt(address);


                    string valStr = kv.Value?.Trim() ?? "";
                    string lower = valStr.ToLowerInvariant();
                    int expected;
                    bool isFloat = false;


                    if (lower is "true" or "1" or "yes")
                        expected = 1;
                    else if (lower is "false" or "0" or "no")
                        expected = 0;
                    else if (int.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out int intParsed))
                        expected = intParsed;
                    else if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float floatVal))
                    {
                        byte[] fbytes = BitConverter.GetBytes(floatVal);
                        expected = BitConverter.ToInt32(fbytes, 0);
                        isFloat = true;
                    }
                    else
                    {
                        skippedNoValue++;
                        continue;
                    }


                    if (current != expected)
                    {
                        if (WriteViaOffsetRaw(address, expected))
                            reapplied++;
                    }
                }


                if (reapplied > 0)
                    ConsoleService.Instance.Log($"Watchdog: reapplied {reapplied} flags (skipped no-offset={skippedNoOffset}, no-value={skippedNoValue})", "Info");
            }
            catch { }
        }


        ConsoleService.Instance.Log("Watchdog: stopped", "Info");
    }
}
