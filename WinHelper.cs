using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SherlockMacro;

/// <summary>
/// เทียบเท่า WinExist("ahk_pid ..."), WinGetList(), WinGetProcessName() ฯลฯ ในต้นฉบับ AHK
/// </summary>
public static class WinHelper
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>คืนรายการ (hwnd, pid, title) ของทุกหน้าต่างที่ visible และมี title — เทียบเท่า WinGetList()</summary>
    public static List<(IntPtr Hwnd, int Pid, string Title)> EnumerateTopLevelWindows()
    {
        var results = new List<(IntPtr, int, string)>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            int len = GetWindowTextLength(hwnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            results.Add((hwnd, (int)pid, sb.ToString()));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>เทียบเท่า WinExist("ahk_pid " pid) — คืน handle หน้าต่างหลักของโปรเซส
    /// (รวมหน้าต่างที่ถูกซ่อนอยู่ด้วย ไม่งั้นพอสั่งซ่อนแล้วจะหา handle ไม่เจออีกเลย แก้บั๊ก
    /// "ToggleGameWindow กดซ่อนแล้วกดแสดงกลับไม่ได้" ที่เกิดจากจุดนี้โดยตรง)</summary>
    public static IntPtr FindMainWindowByPid(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (proc.MainWindowHandle != IntPtr.Zero)
                return proc.MainWindowHandle;
        }
        catch
        {
            // โปรเซสอาจปิดไปแล้ว
        }

        // fallback 1: กรณี MainWindowHandle ยังไม่ถูก cache (เช่นเพิ่งเปลี่ยน state) ให้ไล่หาเองจาก EnumWindows
        // (เฉพาะหน้าต่างที่ visible อยู่)
        foreach (var (hwnd, winPid, _) in EnumerateTopLevelWindows())
        {
            if (winPid == pid) return hwnd;
        }

        // fallback 2: หน้าต่างอาจถูกซ่อนอยู่ (เช่นเพิ่งกด ToggleGameWindow ไปซ่อนไว้) —
        // ต้องหาแบบไม่กรอง visible ด้วย ไม่งั้นจะไม่มีทางหา handle เจออีกเลยตราบใดที่ยังซ่อนอยู่
        return FindAnyWindowByPid(pid);
    }

    /// <summary>หาหน้าต่างระดับบนสุดตัวแรกของ PID นี้ โดยไม่สนว่าจะ visible หรือซ่อนอยู่ก็ตาม
    /// ข้าม "GDI+ Window" ไปเสมอ (เป็นหน้าต่างลูกที่เกมสร้างแยกต่างหาก ไม่ใช่หน้าต่างหลักที่ต้องการ)</summary>
    private static IntPtr FindAnyWindowByPid(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint winPid);
            if (winPid != (uint)pid) return true;

            int len = GetWindowTextLength(hwnd);
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                if (sb.ToString().Contains("GDI+", StringComparison.OrdinalIgnoreCase)) return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>เทียบเท่า ProcessExist(pid)</summary>
    public static bool ProcessExists(int pid)
    {
        try
        {
            Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>เทียบเท่า GetWindowThreadProcessId บนหน้าต่างที่ active อยู่ตอนนี้ (foreground)</summary>
    public static int GetForegroundPid()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    /// <summary>
    /// เทียบเท่า DetectHiddenWindows(True) + WinExist("titlePrefix ahk_pid " pid) — หาแม้หน้าต่างซ่อนอยู่
    /// (ต่างจาก EnumerateTopLevelWindows ที่กรองเฉพาะหน้าต่างที่ visible เท่านั้น)
    /// </summary>
    public static IntPtr FindWindowByTitlePrefixAndPid(string titlePrefix, int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            int len = GetWindowTextLength(hwnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (!sb.ToString().StartsWith(titlePrefix, StringComparison.Ordinal)) return true;

            Native.GetWindowThreadProcessId(hwnd, out uint winPid);
            if (winPid != (uint)pid) return true;

            found = hwnd;
            return false; // เจอแล้ว หยุด enum
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>รอจนกว่าหน้าต่างนี้จะกลายเป็น foreground จริงๆ (สูงสุด timeoutMs) — เทียบเท่า WinWaitActive</summary>
    public static bool WaitForForeground(IntPtr hwnd, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (Native.GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(10);
        }
        return Native.GetForegroundWindow() == hwnd;
    }
}
