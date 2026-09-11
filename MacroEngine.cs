using System.Diagnostics;

namespace SherlockMacro;

/// <summary>
/// หัวใจของสคริปต์ — เทียบเท่า HPAutoPot(), SPAutoPot(), ProcessAllBuffs(), ProcessTimerLoop(),
/// RunMacro(), EmergencyEscLogic() ในต้นฉบับ AHK รวมกันไว้ที่เดียว
///
/// ต่างจาก AHK ตรงที่นี่ "ขนานกันจริง" — HPAutoPot กับ SPAutoPot กับ ProcessAllBuffs
/// รันอยู่บน thread คนละตัวจาก ThreadPool พร้อมกันได้เลย ไม่ต้องมีระบบ priority/แซงคิว
/// แบบที่ AHK ต้องใช้ (เพราะ AHK มี thread เดียว)
/// </summary>
public sealed class MacroEngine : IDisposable
{
    public GameMemory? Memory { get; private set; }
    public long HPAddr { get; private set; }
    public long SPAddr => HPAddr + 0x8;
    public long BuffBase => HPAddr + 0x478;
    public const int MaxBuffSlots = 60;

    public bool IsRunning { get; private set; }
    public bool IsSelected { get; private set; }

    /// <summary>หยุดการทำงานของทุก timer ชั่วคราวโดยไม่ต้อง Stop() จริง — ใช้ตอนสลับไปคลิก/ขยับเมาส์ในเกม
    /// ด้วยมือ (MoveToEsc/MoveMouseToGameCenter) ไม่ให้ macro แย่งบังคับคีย์/เมาส์ระหว่างนั้น
    /// เทียบเท่าการเซฟค่า IsRunning เดิมไว้ชั่วคราวแล้วตั้งเป็น false ในต้นฉบับ AHK</summary>
    public bool Paused { get; set; }

    // ค่าที่ MainForm ผูกเข้ามาจาก UI (แทน Global EditHpLimit.Value เป็นต้น)
    public Func<double>? GetHpLimitPercent;
    public Func<double>? GetSpLimitPercent;
    public Func<string>? GetHpKey;
    public Func<string>? GetSpKey;
    public Func<bool>? GetAutoClickEnabled;
    public Func<bool>? GetAutoSpaceEnabled;
    public Func<bool>? GetEmergencyEscEnabled;
    public Func<double>? GetEmergencyEscThreshold;
    public Func<IReadOnlyList<MacroRow>>? GetMacroRows;
    public Func<IReadOnlyList<MacroRow>>? GetTimerRows;
    public Func<IReadOnlyList<BuffEntry>>? GetBuffEntries;

    /// <summary>ผูกเข้ากับ MoveToEsc()/MoveMouseToGameCenter() ตัวจริงใน MainForm — ใช้แทนการยิง
    /// Esc+คลิก background แบบเดิมตอน HP ใกล้หมด เพราะสองฟังก์ชันนี้คลิกขวาจริงตรงตำแหน่งที่ถูกต้อง</summary>
    public Action? MoveToEscAction;
    public Action? MoveMouseToGameCenterAction;

    public event Action<string>? StatusMessage; // เทียบเท่า ToolTip(...)

    // ต้องระบุ System.Threading.Timer แบบเต็ม เพราะ using ของ WinForms SDK ทำให้
    // "Timer" เฉยๆ ชนกับ System.Windows.Forms.Timer (ตัวที่ใช้ในหน้า GUI สำหรับ UpdateStatusUi)
    private System.Threading.Timer? _hpTimer, _spTimer, _buffTimer, _rowTimerLoop, _escTimer;
    private Thread? _macroThread;
    private volatile bool _macroThreadShouldRun;

    private readonly Dictionary<string, long> _lastBuffPress = new();
    private readonly Dictionary<string, long> _buffMissingSince = new();
    private long _lastEscTick;
    private volatile bool _escInProgress;
    private uint _lastKnownMaxHp;

    public bool Connect(int pid, long hpAddr)
    {
        Memory?.Dispose();
        Memory = new GameMemory(pid);
        if (!Memory.IsValid)
        {
            Memory = null;
            return false;
        }
        HPAddr = hpAddr;
        IsSelected = true;
        return true;
    }

    public void ResetSelection()
    {
        Stop();
        IsSelected = false;
        Memory?.Dispose();
        Memory = null;
    }

    public (uint cur, uint max) ReadHp()
    {
        if (Memory is null) return (0, 0);
        return (Memory.ReadUInt(HPAddr), Memory.ReadUInt(HPAddr + 0x4));
    }

    public (uint cur, uint max) ReadSp()
    {
        if (Memory is null) return (0, 0);
        return (Memory.ReadUInt(SPAddr), Memory.ReadUInt(SPAddr + 0x4));
    }

    public string ReadCharacterName(long nameAddr) => Memory?.ReadString(nameAddr, 32) ?? "Unknown";

    /// <summary>เทียบเท่า IsBuffActive(TargetID) — ไล่สแกน slot buff ทั้งหมดหา ID ที่ตรงกัน</summary>


    private static bool TryToInt(object v, out int result)
    {
        switch (v)
        {
            case int i: result = i; return true;
            case string s when int.TryParse(s, out int parsed): result = parsed; return true;
            default: result = 0; return false;
        }
    }

    // ---------------------------------------------------------------
    // Start / Stop — เทียบเท่า ToggleMacro() / StopMacro()
    // ---------------------------------------------------------------

    public void Start()
    {
        if (IsRunning || Memory is null) return;
        IsRunning = true;

        // แต่ละ timer นี้คือ ThreadPool thread แยกกันจริงๆ — ทำงานขนานได้ ไม่ต้องมี priority
        _hpTimer = new System.Threading.Timer(_ => SafeInvoke(HPAutoPot), null, 0, 10);
        _spTimer = new System.Threading.Timer(_ => SafeInvoke(SPAutoPot), null, 0, 10);
        _buffTimer = new System.Threading.Timer(_ => SafeInvoke(ProcessAllBuffs), null, 0, 20);
        _rowTimerLoop = new System.Threading.Timer(_ => SafeInvoke(ProcessTimerRows), null, 0, 100);
        _escTimer = new System.Threading.Timer(_ => SafeInvoke(EmergencyEscLogic), null, 0, 100);

        _macroThreadShouldRun = true;
        _macroThread = new Thread(RunMacroLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _macroThread.Start();

        StatusMessage?.Invoke("🚀 เริ่มทำงาน");
    }

    public void Stop()
    {
        IsRunning = false;
        _macroThreadShouldRun = false;

        _hpTimer?.Dispose(); _hpTimer = null;
        _spTimer?.Dispose(); _spTimer = null;
        _buffTimer?.Dispose(); _buffTimer = null;
        _rowTimerLoop?.Dispose(); _rowTimerLoop = null;
        _escTimer?.Dispose(); _escTimer = null;

        _macroThread?.Join(500);
        _macroThread = null;

        StatusMessage?.Invoke("🛑 หยุดการทำงาน");
    }

    private string? _lastSafeInvokeError;

    private void SafeInvoke(Action a, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(a))] string? name = null)
    {
        try
        {
            a();
            _lastSafeInvokeError = null; // ทำงานผ่านแล้ว เคลียร์ error เก่าทิ้ง จะได้แจ้งซ้ำได้ถ้า error อีกรอบ
        }
        catch (Exception ex)
        {
            // เดิม catch เงียบๆ ทิ้งหมดเลย ทำให้ error ทุกชนิด (เช่น NullReferenceException)
            // ดูเหมือน "ฟังก์ชันไม่ทำงานเฉยๆ" ไม่มีทางรู้เลยว่าพังตรงไหน — ตอนนี้โชว์ผ่าน
            // StatusMessage แทน แต่กันสแปม (ฟังก์ชันพวกนี้ถูกเรียกทุก 10-100ms) ด้วยการแจ้ง
            // เฉพาะตอนข้อความ error เปลี่ยนไปจากครั้งก่อนเท่านั้น
            string msg = $"{name}: {ex.Message}";
            if (msg != _lastSafeInvokeError)
            {
                _lastSafeInvokeError = msg;
                StatusMessage?.Invoke($"⚠️ {msg}");
            }
        }
    }

    // ---------------------------------------------------------------
    // HPAutoPot / SPAutoPot
    // ---------------------------------------------------------------

    private void HPAutoPot()
    {
        if (!IsRunning || Paused || Memory is null || HPAddr == 0) return;

        var (curHp, maxHp) = ReadHp();
        double limit = GetHpLimitPercent?.Invoke() ?? 0;
        if (maxHp == 0) return;

        double percent = curHp / (double)maxHp * 100;
        if (percent < limit)
        {
            string key = GetHpKey?.Invoke() ?? "";
            KeySender.SendKeyBackground(key, Memory.Pid);
        }
    }

    private void SPAutoPot()
    {
        if (!IsRunning || Paused || Memory is null || HPAddr == 0) return;

        string key = GetSpKey?.Invoke() ?? "";
        if (string.IsNullOrEmpty(key) || key == "None") return;

        var (curSp, maxSp) = ReadSp();
        if (maxSp == 0) return;

        double limit = GetSpLimitPercent?.Invoke() ?? 0;
        double percent = curSp / (double)maxSp * 100;
        if (percent < limit)
            KeySender.SendKeyBackground(key, Memory.Pid);
    }

    public bool IsBuffActive(object targetId)
    {
        if (Memory is null || BuffBase == 0) return false;
        if (!TryToInt(targetId, out int tId)) return false;

        for (int i = 0; i < MaxBuffSlots; i++)
        {
            uint currentBuffId = Memory.ReadUInt(BuffBase + i * 4);
            if (currentBuffId == (uint)tId) return true;
        }
        return false;
    }


    // ---------------------------------------------------------------
    // ProcessAllBuffs — เทียบเท่า ProcessAllBuffs() ในต้นฉบับ
    // ---------------------------------------------------------------

private void ProcessAllBuffs()
    {
        if (!IsRunning || Paused || Memory is null) return;
        var entries = GetBuffEntries?.Invoke();
        if (entries is null) return;

        foreach (var ep in entries)
        {
            string currentKey = ep.EffectiveKey;
            if (string.IsNullOrEmpty(currentKey) || currentKey == "None") continue;

            string keyName = $"EP_{ep.FileName}_{currentKey}";
            bool foundActive = false, isTargetBuff = false, isIdZero = false;

            foreach (var id in ep.Ids)
            {
                if (id is string s && (s == "" || s == "None")) continue;
                if (Equals(id, 0) || Equals(id, "0")) { isIdZero = true; foundActive = false; break; }
                if (Equals(id, 1154) || Equals(id, "1154")) isTargetBuff = true;
                if (IsBuffActive(id)) { foundActive = true; break; }
            }

            bool shouldPress = false;
            long missingThreshold = 20, currentCooldown = 20;
            if (isTargetBuff) { missingThreshold = 50; currentCooldown = 250; }
            
            // กำหนด Cooldown เฉพาะสำหรับ Debuff ให้เว้นช่วงประมาณ 1 วินาที ป้องกันการรัวคีย์
            if (ep.IsDebuff) { currentCooldown = 10; }

            long now = Environment.TickCount64;

            if (!ep.IsDebuff)
            {
                if (foundActive)
                {
                    _buffMissingSince[keyName] = 0;
                }
                else if (isIdZero)
                {
                    shouldPress = true;
                }
                else
                {
                    if (!_buffMissingSince.TryGetValue(keyName, out long since) || since == 0)
                        _buffMissingSince[keyName] = now;

                    if (now - _buffMissingSince[keyName] > missingThreshold)
                        shouldPress = true;
                }
            }
            else
            {
                // สำหรับ Debuff: ถ้าพบว่าติดสถานะ ให้สั่งกดใช้ไอเทมแก้ทันที
                if (foundActive)
                {
                    shouldPress = true;
                }
                else
                {
                    _buffMissingSince[keyName] = 0;
                }
            }

            if (shouldPress)
            {
                long lastTime = _lastBuffPress.GetValueOrDefault(keyName, 0);
                long finalCooldown = isIdZero ? 30000 : currentCooldown;
                if (now - lastTime > finalCooldown)
                {
                    KeySender.SendKeyBackground(currentKey, Memory.Pid);
                    _lastBuffPress[keyName] = now;
                    _buffMissingSince[keyName] = 0;
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // ProcessTimerLoop — แถวในแท็บ TIMER ที่กดคีย์ซ้ำตามรอบเวลา (วินาที/นาที)
    // ---------------------------------------------------------------
private readonly Dictionary<string, long> _lastTimerPress = new();

    private void ProcessTimerRows()
    {
        if (!IsRunning || Paused || Memory is null) return;
        var rows = GetTimerRows?.Invoke();
        if (rows is null) return;

        long now = Environment.TickCount64;
        int index = 0;
        foreach (var row in rows)
        {
            string key = row.EffectiveKey;
            if (!row.IsTimer || string.IsNullOrEmpty(key)) continue;

            string timerKey = $"Timer_{index}_{key}";

            double delayMs = row.EffectiveTimerUnit == "min" ? row.EffectiveDelayMs * 60_000.0 : row.EffectiveDelayMs * 1000.0;
            if (delayMs < 10) delayMs = 10;

            long lastSent = _lastTimerPress.GetValueOrDefault(timerKey, 0);
            if (now - lastSent >= delayMs)
            {
                KeySender.SendKeyBackground(key, Memory.Pid);
                _lastTimerPress[timerKey] = now;
            }
            index++;
        }
    }
    // ---------------------------------------------------------------
    // RunMacro — ไล่ยิงคีย์ในแท็บ MACRO ทีละแถวเป็น loop ต่อเนื่อง (เทียบเท่า while(IsRunning) เดิม)
    // ---------------------------------------------------------------

    private void RunMacroLoop()
    {
        while (_macroThreadShouldRun && IsRunning)
        {
            if (Paused || _escInProgress) { Thread.Sleep(20); continue; }

            if (Memory is not null)
            {
                IntPtr hwnd = WinHelper.FindMainWindowByPid(Memory.Pid);
                if (hwnd != IntPtr.Zero)
                {
                    if (GetAutoClickEnabled?.Invoke() == true)
                        KeySender.SendClickBackground(hwnd, 0, 0);

                    if (GetAutoSpaceEnabled?.Invoke() == true)
                        KeySender.SendKeyBackground("Space", Memory.Pid);

                    var rows = GetMacroRows?.Invoke();
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            if (!_macroThreadShouldRun || !IsRunning) break;
                            if (row.IsTimer) continue;
                            if (string.IsNullOrEmpty(row.EffectiveKey) || row.EffectiveKey == "None") continue;
                            ExecuteRow(row, hwnd);
                        }
                    }
                }
            }
            
        }
    }

    private void ExecuteRow(MacroRow row, IntPtr hwnd)
    {
        if (Memory is null) return;
        string key = row.EffectiveKey;
        int vk = KeySender.GetVirtualKey(key);
        uint sc = KeySender.GetScanCode(key);
        if (vk == 0) return;

        for (int i = 0; i < row.EffectiveRepeatCount; i++)
        {
            if (!IsRunning) break;

            if (row.EffectiveClickToo)
            {
                for (int c = 0; c < 4 && IsRunning; c++)
                {
                    Native.PostMessage(hwnd, Native.WM_KEYDOWN, (IntPtr)vk, Native.MakeKeyDownLParam(sc));
                    Thread.Sleep(1);
                    Native.PostMessage(hwnd, Native.WM_KEYUP, (IntPtr)vk, Native.MakeKeyUpLParam(sc));
                    Thread.Sleep(1);
                    if (!IsRunning) break;
                    KeySender.SendClickBackground(hwnd, 0, 0);
                    Thread.Sleep(1);
                }
            }
            else
            {
                Native.PostMessage(hwnd, Native.WM_KEYDOWN, (IntPtr)vk, Native.MakeKeyDownLParam(sc));
                Thread.Sleep(1);
                Native.PostMessage(hwnd, Native.WM_KEYUP, (IntPtr)vk, Native.MakeKeyUpLParam(sc));
            }

            if (row.EffectiveSpaceToo)
            {
                for (int s = 0; s < 4 && IsRunning; s++)
                {
                    KeySender.SendKeyBackground("Space", Memory.Pid);
                    Thread.Sleep(1);
                }
            }

            if (row.EffectiveDelayMs > 0) Thread.Sleep(row.EffectiveDelayMs);
        }
    }

    // ---------------------------------------------------------------
    // Emergency ESC — เทียบเท่า EmergencyEscLogic()
    // ---------------------------------------------------------------

    private void EmergencyEscLogic()
    {
        if (!IsRunning || Paused || _escInProgress || Memory is null || GetEmergencyEscEnabled?.Invoke() != true) return;
        var (curHp, maxHp) = ReadHp();

        // จำค่า maxHp ล่าสุดที่ถูกต้อง (มากกว่า 0) ไว้ก่อนเช็คเงื่อนไขใดๆ
        if (maxHp > 0) _lastKnownMaxHp = maxHp;

        double percent;
        if (maxHp == 0)
        {
            // ถ้าไม่เคยเห็นค่า maxHp ที่ถูกต้องมาก่อนเลย แปลว่ายังไม่ได้เข้าเกม/ยังไม่มีตัวละคร
            // ไม่ใช่ตายจริง จึงยังไม่ต้องทำอะไร
            if (_lastKnownMaxHp == 0) return;

            // แต่ถ้าเคยเห็นค่าจริงมาก่อนแล้ว (เข้าเกมอยู่) แล้วจู่ๆ maxHp กลายเป็น 0
            // ตอนนี้ ส่วนใหญ่คือตัวละครตายแล้ว (memory ของ HP struct เคลียร์เป็น 0 ทั้งคู่)
            // ให้ถือว่า HP = 0% ทันที ไม่ return ทิ้งแบบเดิม เพราะเคสนี้แหละที่ต้องใช้ไอเทมชุบชีวิตมากที่สุด
            percent = 0;
        }
        else
        {
            percent = curHp / (double)maxHp * 100;
        }

        double threshold = GetEmergencyEscThreshold?.Invoke() ?? 1;
        long now = Environment.TickCount64;
        if (percent < threshold && now - _lastEscTick > 3500)
        {
            _escInProgress = true;
            IntPtr previousForeground = Native.GetForegroundWindow();
            try
            {
                // ยิงไอเทมชุบชีวิตซ้ำไปเรื่อยๆ จนกว่า HP จะฟื้นเกิน threshold จริง (ทำ Esc+click
                // ปิด dialog ทุกรอบด้วย เผื่อ dialog เด้งขึ้นมาใหม่ทุกครั้งที่ตาย ไม่ใช่แค่ครั้งแรก)
                // เว้น 3.5 วิระหว่างแต่ละรอบ (กันเปลืองไอเทม เหมือนกับ cooldown ตอนแรกที่ตั้งไว้)
                // ยังไม่เรียก MoveMouseToGameCenter ระหว่างที่ยังไม่ฟื้น เพราะควรทำแค่ครั้งเดียว
                // หลังฟื้นแล้วเท่านั้น ไม่ใช่ทุกรอบที่ลองยิง
                while (IsRunning)
                {
                    var (hp2, maxHp2) = ReadHp();
                    double percent2 = maxHp2 > 0 ? hp2 / (double)maxHp2 * 100 : 0;
                    if (percent2 > threshold) break;

                    KeySender.SendKeyBackground("Esc", Memory.Pid);
                    Thread.Sleep(100);
                    IntPtr hwndEsc = WinHelper.FindMainWindowByPid(Memory.Pid);
                    if (hwndEsc != IntPtr.Zero)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            KeySender.SendClickBackground(hwndEsc, 0, 0);
                            Thread.Sleep(10);
                        }
                    }
                    else
                    {
                        // หา window handle ของเกมไม่เจอ (Memory.Pid=? อาจเปลี่ยน process ไปแล้ว
                        // หรือ FindMainWindowByPid หาไม่เจอจริงๆ) แจ้งให้เห็นแทนที่จะข้ามเงียบๆ
                        StatusMessage?.Invoke($"⚠️ EmergencyEsc: หา window ของ PID {Memory.Pid} ไม่เจอ ข้ามคลิก 4 ที");
                    }

                    MoveToEscAction?.Invoke();
                    Thread.Sleep(100);
                }

                // HP ฟื้นเกิน threshold แล้ว ค่อยเรียก MoveMouseToGameCenter ครั้งเดียวตอนจบ
                if (IsRunning) MoveMouseToGameCenterAction?.Invoke();
            }
            finally
            {
                // คืนโฟกัสกลับไปหน้าต่างที่ active อยู่ก่อนเริ่มทำทั้งหมด (ไม่ใช่หน้าต่างโปรแกรมนี้)
                if (previousForeground != IntPtr.Zero)
                {
                    Native.SetForegroundWindow(previousForeground);
                    WinHelper.WaitForForeground(previousForeground, 1000);
                }
                _escInProgress = false;
            }
            _lastEscTick = now;
        }
    }

    public void Dispose()
    {
        Stop();
        Memory?.Dispose();
    }
}
