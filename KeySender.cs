namespace SherlockMacro;

public static class KeySender
{
    public class KeyInfo
    {
        public uint ScanCode { get; set; }
        public bool IsExtended { get; set; }
    }

    private static readonly Dictionary<string, int> VkMap = BuildVkMap();
    private static readonly Dictionary<string, KeyInfo> KeyMap = BuildKeyMap();

    private static Dictionary<string, int> BuildVkMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i <= 12; i++)
            map[$"F{i}"] = 0x70 + (i - 1);

        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            map[c.ToString()] = char.ToUpper(c);

        foreach (char c in "0123456789")
            map[c.ToString()] = c;

        for (int i = 0; i <= 9; i++)
            map[$"NumPad{i}"] = 0x60 + i;

        map["Space"] = 0x20;
        map["Esc"] = 0x1B;
        map["Escape"] = 0x1B;
        map["Enter"] = 0x0D;
        map["Tab"] = 0x09;
        map["Insert"] = 0x2D; map["Ins"] = 0x2D;
        map["Delete"] = 0x2E; map["Del"] = 0x2E;
        map["Home"] = 0x24;
        map["End"] = 0x23;
        map["PageUp"] = 0x21; map["PgUp"] = 0x21;
        map["PageDown"] = 0x22; map["PgDn"] = 0x22;
        map["Up"] = 0x26;
        map["Down"] = 0x28;
        map["Left"] = 0x25;
        map["Right"] = 0x27;
        map["NumPadDecimal"] = 0x6E;
        map["LButton"] = 0x01;
        map["RButton"] = 0x02;

        return map;
    }

    private static Dictionary<string, KeyInfo> BuildKeyMap()
    {
        var map = new Dictionary<string, KeyInfo>(StringComparer.OrdinalIgnoreCase);

        void Add(string k, uint sc, bool ext) => map[k] = new KeyInfo { ScanCode = sc, IsExtended = ext };

        // ตัวอักษร A-Z
        string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        uint[] alphaSc = { 0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C };
        for (int i = 0; i < alpha.Length; i++)
            Add(alpha[i].ToString(), alphaSc[i], false);

        // ตัวเลขแถวบน 0-9
        uint[] dSc = { 0x0B, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A };
        for (int i = 0; i <= 9; i++)
            Add(i.ToString(), dSc[i], false);

        // F1-F12
        uint[] fSc = { 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43, 0x44, 0x57, 0x58 };
        for (int i = 1; i <= 12; i++)
            Add($"F{i}", fSc[i - 1], false);

        // Numpad 0-9
        Add("NumPad0", 0x52, false);
        Add("NumPad1", 0x4F, false);
        Add("NumPad2", 0x50, false);
        Add("NumPad3", 0x51, false);
        Add("NumPad4", 0x4B, false);
        Add("NumPad5", 0x4C, false);
        Add("NumPad6", 0x4D, false);
        Add("NumPad7", 0x47, false);
        Add("NumPad8", 0x48, false);
        Add("NumPad9", 0x49, false);
        Add("NumPadDecimal", 0x53, false);

        // ปุ่มพิเศษ & Navigation
        Add("Space", 0x39, false);
        Add("Esc", 0x01, false);
        Add("Escape", 0x01, false);
        Add("Enter", 0x1C, false);
        Add("Tab", 0x0F, false);
        
        Add("Insert", 0x52, true); Add("Ins", 0x52, true);
        Add("Delete", 0x53, true); Add("Del", 0x53, true);
        Add("Home", 0x47, true);
        Add("End", 0x4F, true);
        Add("PageUp", 0x49, true); Add("PgUp", 0x49, true);
        Add("PageDown", 0x51, true); Add("PgDn", 0x51, true);
        Add("Up", 0x48, true);
        Add("Down", 0x50, true);
        Add("Left", 0x4B, true);
        Add("Right", 0x4D, true);

        return map;
    }

    public static int GetVirtualKey(string keyName)
    {
        if (string.IsNullOrEmpty(keyName)) return 0;
        return VkMap.TryGetValue(keyName.Trim(), out int vk) ? vk : 0;
    }

    public static uint GetScanCode(string keyName)
    {
        return GetScanCode(keyName, out _);
    }

    public static uint GetScanCode(string keyName, out bool isExtended)
    {
        isExtended = false;
        if (string.IsNullOrEmpty(keyName)) return 0;
        
        string trimmed = keyName.Trim();
        if (KeyMap.TryGetValue(trimmed, out var info))
        {
            isExtended = info.IsExtended;
            return info.ScanCode;
        }

        int vk = GetVirtualKey(keyName);
        if (vk == 0) return 0;
        return Native.MapVirtualKey((uint)vk, 0);
    }

    public static void SendKeyBackground(string key, int pid)
    {
        if (string.IsNullOrEmpty(key) || key == "None")
            return;

        IntPtr hwnd = WinHelper.FindMainWindowByPid(pid);
        if (hwnd == IntPtr.Zero) return;

        int vk = GetVirtualKey(key);
        if (vk == 0) return;

        uint sc = GetScanCode(key, out bool isExtended);

        int scInt = (int)sc;
        IntPtr lParamDown = (IntPtr)((scInt << 16) | 1 | (isExtended ? (1 << 24) : 0));
        IntPtr lParamUp = (IntPtr)((scInt << 16) | unchecked((int)0xC0000001) | (isExtended ? (1 << 24) : 0));

        Native.PostMessage(hwnd, Native.WM_KEYDOWN, (IntPtr)vk, lParamDown);
        Native.PostMessage(hwnd, Native.WM_KEYUP, (IntPtr)vk, lParamUp);
    }

    public static void SendClickBackground(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr lp = Native.MakeLParamPoint(x, y);
        Native.PostMessage(hwnd, Native.WM_LBUTTONDOWN, (IntPtr)1, lp);
        Thread.Sleep(1);
        Native.PostMessage(hwnd, Native.WM_LBUTTONUP, IntPtr.Zero, lp);
    }
}