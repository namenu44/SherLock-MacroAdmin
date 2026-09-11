using System.Collections.Concurrent;

namespace SherlockMacro;

public enum SpamMode { Off, Click, NoClick }

/// <summary>
/// เทียบเท่าระบบ NormalSlotKeys / SpamActionHandler / ExecuteSpam ในต้นฉบับ AHK:
/// ผูก physical key เข้ากับโหมด spam (Click / NoClick) แล้วขณะที่ผู้เล่นกดค้างคีย์นั้นจริงๆ
/// (และหน้าต่างเกมเป้าหมายกำลัง active) ระบบจะยิงคีย์+คลิกซ้ำไปเรื่อยๆ จนกว่าจะปล่อยคีย์
///
/// จุดต่างจาก AHK: แต่ละคีย์ที่กำลัง spam อยู่รันบน Thread แยกกันจริง (ขนานแท้)
/// แทนที่จะพึ่ง #MaxThreadsPerHotkey ของ AHK ที่ยังต้องแย่ง time-slice ของ interpreter เดียว
/// </summary>
public sealed class SpamSystem : IDisposable
{
    private readonly GlobalKeyboardHook _hook = new();
    private readonly ConcurrentDictionary<int, SpamMode> _slots = new(); // vkCode -> mode
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    public Func<MacroEngine?>? GetEngine;

    public SpamSystem()
    {
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;
        _hook.Install();
    }

    /// <summary>เทียบเท่า UpdateSlot(i, checkboxValue)</summary>
    public void SetSlot(string keyName, SpamMode mode)
    {
        int vk = KeySender.GetVirtualKey(keyName);
        
        // เผื่อกรณีเคสคีย์ตัวเลขเดี่ยวๆ (เช่น "1"-"9") หรือปุ่มตัวอักษรเดี่ยวๆ ที่อาจหลุดรอดการแปลง
        if (vk == 0 && !string.IsNullOrEmpty(keyName))
        {
            if (keyName.Length == 1)
            {
                char c = char.ToUpper(keyName[0]);
                if (c is >= '0' and <= '9' or >= 'A' and <= 'Z')
                {
                    vk = c; // รหัส ASCII/Virtual-Key ตรงกันพอดี
                }
            }
        }

        if (vk == 0) return;

        if (mode == SpamMode.Off)
            _slots.TryRemove(vk, out _);
        else
            _slots[vk] = mode;
    }

    /// <summary>เทียบเท่า ClearSpamSystem() / ClearNumericSpamSystem()</summary>
    public void ClearAll()
    {
        _slots.Clear();
        foreach (var cts in _running.Values) cts.Cancel();
        _running.Clear();
    }

    private void OnKeyDown(int vk)
    {
        var engine = GetEngine?.Invoke();
        if (engine is null || !engine.IsRunning || engine.Memory is null) return;
        if (!_slots.TryGetValue(vk, out var mode)) return;
        if (_running.ContainsKey(vk)) return; // กำลัง spam อยู่แล้ว
        if (WinHelper.GetForegroundPid() != engine.Memory.Pid) return; // ต้องโฟกัสหน้าต่างเกมเท่านั้น

        var cts = new CancellationTokenSource();
        if (!_running.TryAdd(vk, cts)) return;

        // เทียบเท่า SpamActionHandler + while(GetKeyState(...,"P")) loop — แต่รันบน thread จริงแยกกัน
        Task.Run(() => SpamLoop(vk, mode, cts.Token));
    }

    private void OnKeyUp(int vk)
    {
        if (_running.TryRemove(vk, out var cts))
            cts.Cancel();
    }

    private void SpamLoop(int vk, SpamMode mode, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var engine = GetEngine?.Invoke();
                if (engine?.Memory is null || !engine.IsRunning) break;

                ExecuteSpam(vk, mode, engine);

                // เทียบเท่า NtDelayExecution(-10000) ~= 1ms ในต้นฉบับ
                Thread.Sleep(1);
            }
        }
        finally
        {
            _running.TryRemove(vk, out _);
        }
    }

    private static void ExecuteSpam(int vk, SpamMode mode, MacroEngine engine)
    {
        if (engine.Memory is null) return;
        var hwnd = WinHelper.FindMainWindowByPid(engine.Memory.Pid);
        if (hwnd == IntPtr.Zero) return;

        uint sc = Native.MapVirtualKey((uint)vk, 0);
        Native.PostMessage(hwnd, Native.WM_KEYDOWN, (IntPtr)vk, Native.MakeKeyDownLParam(sc));
        Native.PostMessage(hwnd, Native.WM_KEYUP, (IntPtr)vk, Native.MakeKeyUpLParam(sc));

        if (mode == SpamMode.Click)
        {
            Native.GetCursorPos(out var pt);
            KeySender.SendClickBackground(hwnd, pt.X, pt.Y);
        }
    }

    public void Dispose()
    {
        ClearAll();
        _hook.Dispose();
    }
}