using System.Runtime.InteropServices;

namespace SherlockMacro;

/// <summary>
/// เทียบเท่า #UseHook ในต้นฉบับ AHK ที่ทำให้ hotkey ทำงานผ่าน low-level keyboard hook
/// แทนที่จะพึ่ง window message queue ปกติ (จับคีย์ได้แม้โฟกัสอยู่หน้าต่างอื่น)
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc; // ต้อง keep reference ไว้ ไม่งั้นโดน GC แล้ว hook พัง

    /// <summary>เรียกเมื่อมีการกดคีย์ลง — คืนค่า vkCode</summary>
    public event Action<int>? KeyDown;

    /// <summary>เรียกเมื่อมีการปล่อยคีย์ — คืนค่า vkCode</summary>
    public event Action<int>? KeyUp;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)data.vkCode;
            int msg = wParam.ToInt32();

            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN) KeyDown?.Invoke(vk);
            else if (msg is WM_KEYUP or WM_SYSKEYUP) KeyUp?.Invoke(vk);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
