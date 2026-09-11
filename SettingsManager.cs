using System.Text.Json;

namespace SherlockMacro;

/// <summary>เซิร์ฟเวอร์หนึ่งรายการที่ผู้ใช้เพิ่มผ่านหน้า "Add New Server" — เทียบเท่าหนึ่งช่วง
/// "exe|hpAddr|nameAddr" ในสาย MultiServer.List ของ setting.ini เดิม</summary>
public sealed class ServerEntry
{
    public string Exe { get; set; } = "";
    public string HpAddr { get; set; } = "";   // เก็บเป็น string hex "0x..." เหมือนที่ผู้ใช้กรอก
    public string NameAddr { get; set; } = "";
}

public sealed class AppSettings
{
    public List<ServerEntry> Servers { get; set; } = new();
}

/// <summary>เทียบเท่า setting.ini ในต้นฉบับ AHK (ส่วน [MultiServer] List=...) แต่เก็บเป็น settings.json
/// อ่าน/เขียนง่ายกว่าและไม่ต้อง parse สาย "exe|addr|addr||exe2|..." เอง</summary>
public static class SettingsManager
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));

    public static void AddServer(string exe, string hpAddrHex, string nameAddrHex)
    {
        var settings = Load();
        settings.Servers.Add(new ServerEntry { Exe = exe, HpAddr = hpAddrHex, NameAddr = nameAddrHex });
        Save(settings);
    }

    public static void ClearServers() => Save(new AppSettings());
}
