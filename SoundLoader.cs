using System.Media;
using System.Reflection;

namespace SherlockMacro;

/// <summary>
/// เล่นเสียงที่ฝังเข้า exe โดยตรงผ่าน Embedded Resource — เทียบเท่า SoundPlay() ในต้นฉบับ AHK
/// ที่เล่นเสียงตอนกด ToggleMacro() เริ่ม/หยุด
///
/// วางไฟล์ไว้ที่ SherlockMacro\Sounds\start.wav และ SherlockMacro\Sounds\stop.wav
/// (โฟลเดอร์ "Sounds" เทียบเท่ากับ "Icons" — ตอน build จะถูกฝังเข้า assembly อัตโนมัติ
/// ตามที่กำหนดใน .csproj ด้วย &lt;EmbeddedResource Include="Sounds\**\*.wav" /&gt;)
/// ถ้าไม่มีไฟล์ .wav จะ fallback ไปใช้ SystemSounds เฉยๆ ไม่ error
/// </summary>
public static class SoundLoader
{
    private const string ResourcePrefix = "SherlockMacro.Sounds.";
    private static readonly Assembly Asm = Assembly.GetExecutingAssembly();
    private static readonly Dictionary<string, byte[]?> Cache = new();
    private static readonly string[] AllResourceNames = Asm.GetManifestResourceNames();

    // เก็บ SoundPlayer ไว้ไม่ให้โดน GC ระหว่างเล่นเสียงแบบ async (PlaySync บล็อค UI thread ไม่ได้)
    private static SoundPlayer? _current;

    /// <summary>เล่นไฟล์เสียงตามชื่อ (ไม่ใส่นามสกุล เช่น "start", "stop") ถ้าไม่เจอไฟล์ฝังไว้ จะ fallback
    /// เป็น SystemSounds.Asterisk/Exclamation ให้พอได้ยินแทน ไม่ทำให้โปรแกรมพัง</summary>
    public static void Play(string fileName)
    {
        try
        {
            byte[]? data = LoadBytes(fileName);
            if (data != null)
            {
                _current?.Stop();
                _current = new SoundPlayer(new MemoryStream(data));
                _current.Play(); // async ไม่บล็อค UI
            }
            else
            {
                SystemSounds.Beep.Play();
            }
        }
        catch
        {
            // เทียบเท่า try/catch เงียบๆ ในต้นฉบับ AHK — เสียงพังไม่ควรทำให้ macro หยุดทำงาน
        }
    }

    private static byte[]? LoadBytes(string fileNameNoExt)
    {
        if (Cache.TryGetValue(fileNameNoExt, out var cached)) return cached;

        byte[]? bytes = null;
        string target = $"{ResourcePrefix}{fileNameNoExt}.wav";
        string? resourceName = AllResourceNames.FirstOrDefault(n => n.Equals(target, StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            try
            {
                using var stream = Asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    bytes = ms.ToArray();
                }
            }
            catch { bytes = null; }
        }

        Cache[fileNameNoExt] = bytes;
        return bytes;
    }
}
