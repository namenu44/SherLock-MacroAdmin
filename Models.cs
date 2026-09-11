using SherlockMacro.Controls;

namespace SherlockMacro;

/// <summary>
/// แถวสำหรับ Tab "MACRO" (คีย์ + จำนวนซ้ำ + delay + click/space) หรือ Tab "TIMER" (คีย์ + delay หน่วย sec/min)
/// เทียบเท่า object Row ในต้นฉบับ AHK
/// </summary>
public sealed class MacroRow
{
    public string Key { get; set; } = "";
    public int RepeatCount { get; set; } = 1;
    public int DelayMs { get; set; } = 100;
    public bool ClickToo { get; set; }
    public bool SpaceToo { get; set; }
    public bool IsTimer { get; set; }


    public string TimerUnit { get; set; } = "sec";

    public long LastSentTick { get; set; }

    /// <summary>ค่าคีย์ที่ "ใช้งานจริง" ตอนนี้ — อ่านจาก KeyControl.HotkeyValue สดๆ ถ้ามี UI control ผูกอยู่
    /// (กรณีปกติทุกแถวใน MACRO/TIMER tab) ไม่งั้น fallback ไปที่ Key เฉยๆ
    /// ต้องใช้ตัวนี้แทน Key ตรงๆ ทุกที่ที่ engine อ่านค่าไปส่งเข้าเกม เพราะ Key เป็นแค่ค่าตอนสร้าง
    /// object ครั้งแรก ไม่เคย sync ใหม่จาก UI อีกเลยหลังจากผู้ใช้ตั้ง hotkey ในหน้าโปรแกรม
    /// </summary>
    public string EffectiveKey => KeyControl?.HotkeyValue ?? Key;

    /// <summary>เช่นเดียวกับ EffectiveKey — ClickToo/SpaceToo เป็นแค่ default field ที่ไม่เคย sync จาก
    /// checkbox บนหน้าจอ ต้องอ่านจาก ClickCheck/SpaceCheck.Checked สดๆ เสมอ</summary>
    public bool EffectiveClickToo => ClickCheck?.Checked ?? ClickToo;
    public bool EffectiveSpaceToo => SpaceCheck?.Checked ?? SpaceToo;

    /// <summary>เดิม DelayMs/RepeatCount/TimerUnit เป็นแค่ค่า default ตอนสร้างแถวครั้งแรก (100ms / 1 / "sec")
    /// ไม่เคย sync กับกล่อง Delay/จำนวนซ้ำ/dropdown sec-min บนหน้าจออีกเลย ทำให้ engine ยิงคีย์ตามค่า
    /// default เดิมเสมอไม่ว่าจะแก้ในหน้าโปรแกรมแค่ไหน (เช่น TIMER tab กดครั้งแรกแล้วรอ 100 วิถัดไปเสมอ
    /// ต่อให้ตั้งไว้ 3 วิ) ต้องอ่านค่าสดจาก control ก่อนเหมือน EffectiveKey ด้านบน</summary>
    public int EffectiveRepeatCount => (int)(CountControl?.Value ?? RepeatCount);
    public int EffectiveDelayMs => int.TryParse(DelayControl?.Text, out int d) ? d : DelayMs;
    public string EffectiveTimerUnit => UnitCombo?.SelectedItem as string ?? TimerUnit;

    // UI control ที่ผูกกับแถวนี้ (เติมตอนสร้าง GUI จริง)
    public Controls.HotkeyBox? KeyControl { get; set; }
    public NumericUpDown? CountControl { get; set; }
    public TextBox? DelayControl { get; set; }
    public CheckBox? ClickCheck { get; set; }
    public CheckBox? SpaceCheck { get; set; }
    public ComboBox? UnitCombo { get; set; }
}

/// <summary>
/// เทียบเท่า EP (ExtraPots) และ BuffRow ในต้นฉบับ AHK — ทั้งสองมีโครงสร้างเหมือนกันทุกประการ
/// (ไอคอน + hotkey + รายชื่อ BuffID ที่ต้องเช็ค + flag ว่าเป็น debuff หรือไม่)
/// </summary>
public sealed class BuffEntry
{
    public required string FileName { get; init; }
    public required object[] Ids { get; init; }
    public bool IsDebuff { get; init; }
    public string Group { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public string Key { get; set; } = "";
    public Controls.HotkeyBox? KeyControl { get; set; }

    /// <summary>เช่นเดียวกับ MacroRow.EffectiveKey — อ่านจาก KeyControl.HotkeyValue สดๆ ก่อนเสมอ</summary>
    public string EffectiveKey => KeyControl?.HotkeyValue ?? Key;
}
