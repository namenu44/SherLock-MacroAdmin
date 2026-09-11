using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace SherlockMacro;

public sealed class GameMemory : IDisposable
{
    public int Pid { get; }
    private readonly IntPtr _hProcess;

    public GameMemory(int pid)
    {
        Pid = pid;
        _hProcess = Native.OpenProcess(Native.PROCESS_ALL_ACCESS, false, (uint)pid);
    }

    public bool IsValid => _hProcess != IntPtr.Zero;

    public uint ReadUInt(long address) => ReadRaw(address, 4) is { } b ? BitConverter.ToUInt32(b, 0) : 0;

    public int ReadInt(long address) => ReadRaw(address, 4) is { } b ? BitConverter.ToInt32(b, 0) : 0;

    private byte[]? ReadRaw(long address, int size)
    {
        if (_hProcess == IntPtr.Zero) return null;
        var buffer = new byte[size];
        if (Native.ReadProcessMemory(_hProcess, (IntPtr)address, buffer, size, out _))
            return buffer;
        return null;
    }

    public string ReadString(long address, int size = 32)
    {
        var buf = ReadRaw(address, size);
        if (buf == null) return "Unknown";
        try
        {
            var encoding = Encoding.GetEncoding(874); // CP874 ภาษาไทย
            int nullIdx = Array.IndexOf(buf, (byte)0);
            int len = nullIdx >= 0 ? nullIdx : buf.Length;
            return encoding.GetString(buf, 0, len);
        }
        catch
        {
            return "Unknown";
        }
    }

    public (long hpAddr, long nameAddr) FindAddresses() => FindAddresses(out _);

    public (long hpAddr, long nameAddr) FindAddresses(out ScanDiagnostics diag)
    {
        string hpPattern = "39 05 ?? ?? ?? ?? 0F 84 ?? ?? ?? ?? B9 ?? ?? ?? ?? A3 ?? ?? ?? ?? E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 8B ?? ?? 39 05 ?? ?? ?? ??";
        string namePattern = "81 F9 ?? ?? ?? ?? 72 2E 81 FA ?? ?? ?? ?? 77 26 81 EA ?? ?? ?? ?? 0F 1F 80";

        var hpPatternBytes = ParsePattern(hpPattern, out var hpMask);
        var namePatternBytes = ParsePattern(namePattern, out var nameMask);

        diag = new ScanDiagnostics();
        long hpAddr = 0;
        long nameAddr = 0;

        // วิธีหลัก: อ่าน Main Module (ตัว exe เกม) ทีเดียวยาวๆ ตั้งแต่ BaseAddress ไป
        // ModuleMemorySize เหมือนสคริปต์ที่เทสแล้วว่าเจอจริง เพราะแพทเทิร์นพวกนี้เป็น
        // static address ที่ถูกอ้างอิงตรงๆ ในโค้ด/ดาต้าของตัว exe หลัก ไม่ใช่ใน heap
        // วิธีนี้อ่านเป็นก้อนเดียวต่อเนื่อง เลยไม่มีปัญหาคาบรอยต่อ region หรือ protection
        // filter แบบที่การไล่ VirtualQueryEx ทีละ region เจอ
        byte[]? moduleBuffer = TryReadMainModule(out diag);
        if (moduleBuffer != null)
        {
            hpAddr = TryExtract(moduleBuffer, hpPatternBytes, hpMask, offset: 2);
            nameAddr = TryExtract(moduleBuffer, namePatternBytes, nameMask, offset: 32);
        }

        // Fallback: ถ้าอ่าน MainModule ไม่ได้ หรือหาไม่เจอในนั้น ค่อยไล่สแกนทั่ว
        // address space ทั้งโปรเซสแบบเดิม เผื่อ pattern ไปอยู่นอก main module
        if (hpAddr == 0 || nameAddr == 0)
        {
            var (fbHp, fbName) = FindAddressesFullScan(hpPatternBytes, hpMask, namePatternBytes, nameMask, ref diag);
            if (hpAddr == 0) hpAddr = fbHp;
            if (nameAddr == 0) nameAddr = fbName;
        }

        return (hpAddr, nameAddr);
    }

    private static long TryExtract(byte[] buffer, byte?[] pattern, bool[] mask, int offset)
    {
        int index = FindPatternInMemory(buffer, buffer.Length, pattern, mask);
        if (index >= 0 && index + offset + 4 <= buffer.Length)
            return BitConverter.ToInt32(buffer, index + offset);
        return 0;
    }

    private byte[]? TryReadMainModule(out ScanDiagnostics diag)
    {
        diag = new ScanDiagnostics();
        try
        {
            using var proc = Process.GetProcessById(Pid);
            IntPtr baseAddress = proc.MainModule?.BaseAddress ?? IntPtr.Zero;
            int size = proc.MainModule?.ModuleMemorySize ?? 0;
            if (baseAddress == IntPtr.Zero || size <= 0) return null;

            byte[] buffer = new byte[size];
            if (Native.ReadProcessMemory(_hProcess, baseAddress, buffer, size, out var read) && read.ToInt64() > 0)
            {
                diag.RegionsScanned = 1;
                diag.TotalRegionsSeen = 1;
                diag.BytesScanned = read.ToInt64();
                return buffer;
            }
            diag.FailedReads = 1;
        }
        catch
        {
            diag.FailedReads = 1;
        }
        return null;
    }

    private (long hpAddr, long nameAddr) FindAddressesFullScan(
        byte?[] hpPatternBytes, bool[] hpMask, byte?[] namePatternBytes, bool[] nameMask, ref ScanDiagnostics diag)
    {
        int maxPatLen = Math.Max(hpPatternBytes.Length, namePatternBytes.Length);

        long hpAddr = 0;
        long nameAddr = 0;

        IntPtr minAddress = IntPtr.Zero;
        IntPtr maxAddress = new IntPtr(0x7FFFFFFF);

        // เก็บ (pattern length - 1) byte ท้ายสุดของ buffer ก่อนหน้า มาพ่วงไว้หน้า buffer
        // ใหม่ก่อนสแกน กัน pattern ถูกตัดขาดพอดีตรงรอยต่อระหว่าง region (VirtualQueryEx
        // คืน region มาเป็นก้อนๆ ถ้าไม่พ่วงไว้ pattern ที่คาบเกี่ยวสองก้อนจะไม่ถูกเจอเลย)
        byte[] carry = Array.Empty<byte>();

        while (minAddress.ToInt64() < maxAddress.ToInt64())
        {
            if (Native.VirtualQueryEx(_hProcess, minAddress, out var mbi, (uint)Marshal.SizeOf(typeof(Native.MEMORY_BASIC_INFORMATION))) == 0)
                break;

            diag.TotalRegionsSeen++;

            // อ้างอิงจากสคริปต์ Cheat Engine ต้นฉบับที่ใช้ AOBScan(pattern, "*X*W*C")
            // เครื่องหมาย * คือ "ไม่สนใจ/ไม่กรอง" ทั้ง Execute/Write/Copy-on-write
            // เท่ากับ CE สแกนทุก region ที่อ่านได้หมด ไม่จำกัดเฉพาะ region ที่ execute
            // หรือ writable เท่านั้น จึงเปลี่ยนมาใช้แนวทางเดียวกัน คือสแกนทุก region
            // ที่ commit แล้ว ยกเว้น PAGE_NOACCESS (0x01, อ่านไม่ได้) และ
            // page ที่ติด PAGE_GUARD (0x100, อ่านแล้ว throw exception)
            const uint PAGE_NOACCESS = 0x01;
            const uint PAGE_GUARD = 0x100;
            bool readable = mbi.Protect != 0 && (mbi.Protect & PAGE_NOACCESS) == 0;
            bool guarded = (mbi.Protect & PAGE_GUARD) != 0;

            bool scannedThisRegion = false;

            if (mbi.State == 0x1000 && readable && !guarded)
            {
                long regionSizeLong = mbi.RegionSize.ToInt64();
                if (regionSizeLong > 0 && regionSizeLong <= 50 * 1024 * 1024)
                {
                    int regionSize = (int)regionSizeLong;
                    byte[] rawBuffer = new byte[regionSize];
                    if (Native.ReadProcessMemory(_hProcess, mbi.BaseAddress, rawBuffer, regionSize, out _))
                    {
                        scannedThisRegion = true;
                        diag.RegionsScanned++;
                        diag.BytesScanned += regionSize;

                        byte[] buffer;
                        if (carry.Length > 0)
                        {
                            buffer = new byte[carry.Length + rawBuffer.Length];
                            Buffer.BlockCopy(carry, 0, buffer, 0, carry.Length);
                            Buffer.BlockCopy(rawBuffer, 0, buffer, carry.Length, rawBuffer.Length);
                        }
                        else
                        {
                            buffer = rawBuffer;
                        }

                        if (hpAddr == 0)
                            hpAddr = TryExtract(buffer, hpPatternBytes, hpMask, offset: 2);

                        if (nameAddr == 0)
                            nameAddr = TryExtract(buffer, namePatternBytes, nameMask, offset: 32);

                        if (hpAddr != 0 && nameAddr != 0)
                            break;

                        int keep = Math.Min(maxPatLen - 1, rawBuffer.Length);
                        carry = keep > 0 ? rawBuffer[^keep..] : Array.Empty<byte>();
                    }
                    else
                    {
                        diag.FailedReads++;
                    }
                }
            }

            if (!scannedThisRegion) carry = Array.Empty<byte>();

            long nextAddress = mbi.BaseAddress.ToInt64() + mbi.RegionSize.ToInt64();
            if (nextAddress <= minAddress.ToInt64() || nextAddress > 0x7FFFFFFF) break;
            minAddress = new IntPtr(nextAddress);
        }

        return (hpAddr, nameAddr);
    }

    public struct ScanDiagnostics
    {
        public int TotalRegionsSeen;
        public int RegionsScanned;
        public int FailedReads;
        public long BytesScanned;
    }

    private static byte?[] ParsePattern(string pattern, out bool[] mask)
    {
        var tokens = pattern.Split(' ');
        var bytes = new byte?[tokens.Length];
        mask = new bool[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "??" || tokens[i] == "?")
            {
                bytes[i] = null;
                mask[i] = true;
            }
            else
            {
                bytes[i] = Convert.ToByte(tokens[i], 16);
                mask[i] = false;
            }
        }
        return bytes;
    }

    private static int FindPatternInMemory(byte[] buffer, int bufferLength, byte?[] pattern, bool[] mask)
    {
        int maxSearchLength = bufferLength - pattern.Length;
        for (int i = 0; i <= maxSearchLength; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (!mask[j] && buffer[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }

    public void Dispose()
    {
        if (_hProcess != IntPtr.Zero)
            Native.CloseHandle(_hProcess);
    }
}