using System.Reflection;

namespace SherlockMacro;

/// <summary>
/// โหลดไอคอนที่ฝังเข้า exe โดยตรงผ่าน Embedded Resource (แทนที่จะอ่านจากไฟล์แยกข้าง exe)
/// เทียบเท่ากับที่ Sherlock.ahk เดิมฝังรูปเข้า exe ด้วย Ahk2Exe-AddResource/FileInstall
///
/// ตอน build ไฟล์ใน SherlockMacro\Icons\*.png และ *.ico จะถูกฝังเข้า assembly โดยอัตโนมัติ
/// (กำหนดใน .csproj ด้วย &lt;EmbeddedResource Include="Icons\**\*.png;Icons\**\*.ico" /&gt;)
/// ชื่อ resource ที่ได้จะเป็น "SherlockMacro.Icons.&lt;ชื่อไฟล์&gt;" เสมอ (MSBuild ตั้งชื่อให้อัตโนมัติ
/// จาก RootNamespace + path ของไฟล์)
/// </summary>
public static class IconLoader
{
    private const string ResourcePrefix = "SherlockMacro.Icons.";
    private static readonly Assembly Asm = Assembly.GetExecutingAssembly();
    private static readonly Dictionary<string, Image?> Cache = new();

    // แคชรายชื่อ resource ทั้งหมดไว้ครั้งเดียว (Assembly.GetManifestResourceNames() ค่อนข้างช้าถ้าเรียกบ่อย)
    private static readonly string[] AllResourceNames = Asm.GetManifestResourceNames();

    /// <summary>
    /// คืนรูปถ้ามี resource ตรงกับชื่อ (ลองทั้งชื่อเต็ม เช่น "252" และแบบตัดคำต่อท้าย "_1"/".png" ออกก่อน)
    /// คืน null ถ้าไม่เจอ — MainForm จะ fallback ไปแสดงเป็น label ข้อความแทน
    /// </summary>
    public static Image? TryLoad(string fileName)
    {
        string key = fileName.Replace(".png", "").Replace("_1", "");
        return LoadFromResource(key, key);
    }

    /// <summary>โหลดไอคอนสำหรับปุ่ม/UI ทั่วไปโดยตรงจากชื่อไฟล์ (เช่น "logo", "start", "stop")</summary>
    public static Image? TryLoadUi(string fileName) => LoadFromResource("ui_" + fileName, fileName);

    private static Image? LoadFromResource(string cacheKey, string fileNameNoExt)
    {
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        Image? img = null;
        string target = $"{ResourcePrefix}{fileNameNoExt}.png";
        string? resourceName = AllResourceNames.FirstOrDefault(n => n.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            try
            {
                using var stream = Asm.GetManifestResourceStream(resourceName);
                if (stream != null) img = Image.FromStream(stream);
            }
            catch { img = null; }
        }

        Cache[cacheKey] = img;
        return img;
    }

    /// <summary>
    /// ลิสต์ไอคอน .png ที่ฝังไว้ทั้งหมด (ไม่รวมนามสกุล) — ใช้หา "ไอคอนที่เหลือ" ที่ไม่ตรงกับ
    /// Buff ID ที่รู้จักอยู่แล้วใน BuffData (เช่น ไอคอนไอเทมอื่นๆ ที่ผู้ใช้เพิ่มเข้ามาเอง)
    /// </summary>
    public static List<string> ListAvailableIconIds() => AllResourceNames
        .Where(n => n.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .Select(n => n.Substring(ResourcePrefix.Length, n.Length - ResourcePrefix.Length - 4))
        .OrderBy(n => n)
        .ToList();

    /// <summary>เทียบเท่า TraySetIcon(GetImageHandle("Main.ico", true)) — หาไฟล์ .ico ที่ฝังไว้สำหรับไอคอนแอป
    /// (Icon ต้องโหลดจาก stream โดยตรง ต่างจาก Image ทั่วไป จึงแยก method)</summary>
    public static Icon? TryLoadAppIcon() => TryLoadIcon("Main") ?? TryLoadIcon("logo");

    /// <summary>โหลด .ico ที่ฝังไว้ตามชื่อไฟล์ตรงๆ (เช่น "Active", "Inactive") — ใช้กับ tray icon ที่
    /// เปลี่ยนตามสถานะ running/paused เทียบเท่า GetImageHandle("Active.ico"/"Inactive.ico", true)</summary>
    public static Icon? TryLoadIcon(string fileName)
    {
        string target = $"{ResourcePrefix}{fileName}.ico";
        string? resourceName = AllResourceNames.FirstOrDefault(n => n.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null) return null;
        try
        {
            using var stream = Asm.GetManifestResourceStream(resourceName);
            return stream != null ? new Icon(stream) : null;
        }
        catch
        {
            return null; // ข้ามไฟล์เสีย
        }
    }
}
