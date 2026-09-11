using System.Text.Json;

namespace SherlockMacro;

public sealed class RowProfileData
{
    public string Key { get; set; } = "";
    public int Count { get; set; } = 1;
    public int Delay { get; set; } = 100;
    public bool Click { get; set; }
    public bool Space { get; set; }
    public string Unit { get; set; } = "sec";
}

/// <summary>DTO ที่เก็บทุกค่าที่ต้องเซฟ/โหลด — เทียบเท่าค่าที่ SaveProfile()/LoadProfile() อ่าน/เขียนในต้นฉบับ
/// เก็บเป็นไฟล์ .json (แทน .ini แบบเดิม) — อ่านเข้าใจง่ายกว่า และ System.Text.Json (de)serialize ให้ตรงๆ
/// โดยไม่ต้องเขียน parser เอง</summary>
public sealed class ProfileData
{
    public string ToggleHotkey { get; set; } = "";
    public string HpLimit { get; set; } = "80";
    public string HpKey { get; set; } = "";
    public string SpLimit { get; set; } = "40";
    public string SpKey { get; set; } = "";

    public Dictionary<int, string> ExtraPotKeys { get; set; } = new();
    public Dictionary<int, string> BuffRowKeys { get; set; } = new();
    public List<RowProfileData> Rows { get; set; } = new();

    public bool AutoClick { get; set; }
    public bool AutoSpace { get; set; }
    public bool AutoEsc { get; set; }

    public Dictionary<int, int> NormalSlots { get; set; } = new();   // index -> 0/1/2 (Off/Click/NoClick)
    public Dictionary<int, int> NumericSlots { get; set; } = new();

    public List<RowProfileData> TimerRows { get; set; } = new();

    /// <summary>ไอคอนที่ผู้ใช้กำหนดเอง (ไม่ตรงกับ BuffData) — key = ชื่อไฟล์ไอคอน, value = "id|hotkey|isDebuff(0/1)"</summary>
    public Dictionary<string, string> CustomIcons { get; set; } = new();
}

/// <summary>เทียบเท่า SaveProfile(N) / LoadProfile(N) / GetProfileList() ในต้นฉบับ AHK — เก็บเป็น .json
/// ในโฟลเดอร์ Profiles\ แทน .ini</summary>
public static class ProfileManager
{
    public static string ProfileDir { get; } = Path.Combine(AppContext.BaseDirectory, "Profiles");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void EnsureProfileDir()
    {
        if (!Directory.Exists(ProfileDir)) Directory.CreateDirectory(ProfileDir);
    }

    public static List<string> GetProfileList()
    {
        EnsureProfileDir();
        var list = new List<string> { "Default" };
        foreach (var file in Directory.GetFiles(ProfileDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name != "Default") list.Add(name);
        }
        return list;
    }

    public static void Save(string profileName, ProfileData data)
    {
        EnsureProfileDir();
        string path = Path.Combine(ProfileDir, $"{profileName}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOpts));
    }

    public static ProfileData Load(string profileName)
    {
        string path = Path.Combine(ProfileDir, $"{profileName}.json");
        if (!File.Exists(path)) return new ProfileData();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
        }
        catch
        {
            return new ProfileData(); // ไฟล์เสีย/เก่ากว่ารูปแบบปัจจุบัน — เริ่มจากค่าว่างแทนที่จะ crash
        }
    }
}
