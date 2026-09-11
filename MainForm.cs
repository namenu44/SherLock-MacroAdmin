using System.Text.RegularExpressions;
using SherlockMacro.Controls;

namespace SherlockMacro;

/// <summary>
/// แถวสำหรับ "ไอคอนที่เหลือ" ในโฟลเดอร์ Icons\ ที่ไม่ตรงกับ Buff ID ที่รู้จักใน BuffData
/// (เช่น ไอคอนไอเทม/potion อื่นๆ) — ให้ผู้ใช้กำหนด ID จริงที่จะเช็คในเกม + hotkey + โหมด เองได้
/// </summary>
public sealed class CustomIconRow
{
    public required string FileName { get; init; }
    public required TextBox IdBox { get; init; }
    public required CheckBox DebuffCheck { get; init; }
    public required Controls.HotkeyBox KeyBox { get; init; }
}

public sealed class MainForm : Form
{
    private readonly MacroEngine _engine = new();
    private readonly SpamSystem _spamSystem;
    private readonly GlobalKeyboardHook _toggleHook = new();
    private bool _toggleKeyPhysicallyDown;

    // --- แถบบนสุด: profile / window select / status ---
    private ComboBox _cbProfile = null!;
    private ComboBox _cbPid = null!;
    private Label _lblStatus = null!;
    private Label _lblHpVal = null!, _lblHpPer = null!, _lblSpVal = null!, _lblSpPer = null!;
    private HotkeyBox _hkToggle = null!;
    private Label _btnToggle = null!;
    private CheckBox _cbLockKey = null!;
    private CheckBox _cbEnableSound = null!;

    // --- Tab 1: MACRO ---
    private readonly List<MacroRow> _macroRows = new();
    private CheckBox _cbAutoClick = null!, _cbAutoSpace = null!, _cbEmergencyEsc = null!;

    // --- Tab 2: AUTOPOT & DEBUFF ---
    private TextBox _edHpLimit = null!, _edSpLimit = null!;
    private HotkeyBox _hkHpKey = null!, _hkSpKey = null!;
    private readonly List<BuffEntry> _extraPots = new();
    private readonly List<CustomIconRow> _customIconRows = new();

    // --- Tab 3: BUFFS ---
    private readonly List<BuffEntry> _buffRows = new();
    private FlowLayoutPanel _buffPanel = null!;
    private ListBox _classList = null!;

    // --- Tab 4: TIMER ---
    private readonly List<MacroRow> _timerRows = new();

    // --- Tab 5: SPAM ---
    private static readonly string[] KeyLabels =
        { "F1","F2","F3","F4","F5","F6","F7","F8","F9","Q","W","E","R","T","Y","U","I","O","P",
          "A","S","D","F","G","H","J","K","L","Z","X","C","V","B","N","M" };
    private static readonly string[] NumericLabels = { "1","2","3","4","5","6","7","8","9" };
    private readonly Dictionary<int, CheckBox> _normalSlotChecks = new();
    private readonly Dictionary<int, CheckBox> _numericSlotChecks = new();

    // --- Tab 6: PROFILES ---
    private TextBox _edLicenseKey = null!;
    private Button _btnLogin = null!;
    private TextBox _edProfileName = null!;
    private ListBox _lbProfiles = null!;

    private long _hpAddr, _nameAddr;
    private System.Windows.Forms.Timer _uiTimer = null!;
    private readonly ToolTip _tips = new();
    private List<TabPage> _lockableTabPages = new();
    private List<Control> _profilesLockControls = new();
    private List<Control> _topBarLockControls = new();
    private bool _isLoggedIn;
    private NotifyIcon _trayIcon = null!;
    private Icon? _iconActive, _iconInactive;
    private string _lastTrayText = "";

    public MainForm()
    {
        Text = "SherLock Macro 2";
        ClientSize = new Size(400, 670); // เท่ากับ MyGui.Show("w400 h670") ในต้นฉบับเป๊ะๆ
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ColorTranslator.FromHtml("#F0F2F5"); // MyGui.BackColor := "F0F2F5"

        // ไอคอนหน้าต่าง/task bar — โหลดจาก resource ที่ฝังไว้ใน exe (ถ้ามี Icons\Main.ico หรือ Icons\logo.ico)
        var appIcon = IconLoader.TryLoadAppIcon();
        if (appIcon != null) Icon = appIcon;

        _spamSystem = new SpamSystem { GetEngine = () => _engine };
        _engine.StatusMessage += msg => BeginInvoke(() => ShowToast(msg));

        // เดิมปุ่ม _hkToggle เก็บค่าคีย์ไว้เฉยๆ แต่ไม่มีอะไรมาคอยดักคีย์จริงเลย — ปุ่มบน GUI เลย
        // เป็นทางเดียวที่กด ToggleMacro() ได้ ตรงนี้เพิ่ม low-level keyboard hook แยกไว้คอยเทียบ
        // vkCode ที่กดจริงกับ _hkToggle.HotkeyValue แล้วสั่ง ToggleMacro() ให้เหมือนกดปุ่มจริง
        _toggleHook.KeyDown += OnGlobalToggleKeyDown;
        _toggleHook.KeyUp += OnGlobalToggleKeyUp;
        _toggleHook.Install();

        BuildTopBar();
        var tabs = BuildTabs();
        Controls.Add(tabs);
        StartGdiWatchTimer();
        BuildTrayIcon();

        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        Load += (_, _) => OnFormLoad();
        FormClosing += (_, _) => { _engine.Dispose(); _spamSystem.Dispose(); _toggleHook.Dispose(); _tips.Dispose(); _gdiWatchTimer?.Dispose(); _trayIcon.Dispose(); };
    }

    // =========================================================================
    // Layout — พิกัด/สี/ฟอนต์ต่างๆ อ้างอิงจากค่า x/y/w/h ในไฟล์ Sherlock.ahk ต้นฉบับ 1:1
    // (ส่วนบนของหน้าต่างใช้พิกัดตรงๆ เหมือน AHK เพราะ Form ก็คือ client area แบบเดียวกับ Gui window;
    //  ส่วนในแท็บต้องหักค่า TabControl's top + แถบหัวข้อแท็บออกก่อน เพราะ WinForms TabPage
    //  ใช้พิกัดสัมพัทธ์กับตัวเอง ไม่ใช่พิกัดสัมบูรณ์เหมือน AHK)
    // =========================================================================

    private static readonly Color ColorTitleBlue = Color.FromArgb(0x00, 0x5A, 0x9E);   // c005A9E
    private static readonly Color ColorHpRed = Color.FromArgb(0xC4, 0x2B, 0x1C);        // cC42B1C
    private static readonly Color ColorSpBlue = Color.FromArgb(0x00, 0x67, 0xC0);       // c0067C0
    private static readonly Color ColorHotkeyPurple = Color.FromArgb(0x8A, 0x2B, 0xE2); // c8A2BE2
    private static readonly Color ColorStatusRed = Color.FromArgb(0xA6, 0x00, 0x00);    // cA60000
    private static readonly Color ColorOffRed = Color.FromArgb(0x80, 0x00, 0x00);       // c800000
    private static readonly Color ColorPinkBg = Color.FromArgb(0xFF, 0xD9, 0xD9);       // BackgroundFFD9D9
    private static readonly Color ColorGreenBg = Color.FromArgb(0xD9, 0xFF, 0xD9);      // BackgroundD9FFD9

    private void BuildTopBar()
    {
        var title = new Label
        {
            Text = "SherLock Macro", Location = new Point(10, 5), AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = ColorTitleBlue
        };

        var lblProfiles = new Label { Text = "Profiles:", Location = new Point(210, 15), AutoSize = true };
        _cbProfile = new ComboBox { Location = new Point(270, 12), Width = 75, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbProfile.SelectedIndexChanged += (_, _) => { if (_cbProfile.SelectedItem is string s) LoadProfile(s); };
        var btnSave = new Button { Text = "💾", Location = new Point(355, 11), Width = 40, Height = 22 };
        btnSave.Click += (_, _) => SaveProfile(_cbProfile.Text);

        var lblWindow = new Label { Text = "Window:", Location = new Point(45, 50), AutoSize = true };
        _cbPid = new ComboBox { Location = new Point(105, 46), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbPid.SelectedIndexChanged += (_, _) => OnPidSelected();
        var btnRefresh = new Button { Text = "🔁 Refresh", Location = new Point(290, 45), Width = 90, Height = 26 };
        btnRefresh.Click += (_, _) => RefreshPidList();

        _lblStatus = new Label
        {
            Text = "DISCONNECTED", Location = new Point(20, 85), Width = 360, Height = 30,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = ColorPinkBg,
            ForeColor = ColorStatusRed, Font = new Font("Segoe UI", 11, FontStyle.Regular)
        };

        var hpIcon = TryMakeUiPictureBox("HP", new Point(20, 130), new Size(20, 20));
        _lblHpVal = new Label { Text = "0 / 0", Location = new Point(45, 130), Width = 150, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ColorHpRed, Font = new Font("Segoe UI", 8) };
        _lblHpPer = new Label { Text = "( 0% )", Location = new Point(45, 150), Width = 150, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ColorHpRed, Font = new Font("Segoe UI", 8) };

        var spIcon = TryMakeUiPictureBox("SP", new Point(20, 190), new Size(20, 20));
        _lblSpVal = new Label { Text = "0 / 0", Location = new Point(45, 190), Width = 150, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ColorSpBlue, Font = new Font("Segoe UI", 8) };
        _lblSpPer = new Label { Text = "( 0% )", Location = new Point(45, 210), Width = 150, TextAlign = ContentAlignment.MiddleCenter, ForeColor = ColorSpBlue, Font = new Font("Segoe UI", 8) };

        var lblSetHk = new Label { Text = "Set Hotkey:", Location = new Point(215, 125), AutoSize = true, ForeColor = ColorHotkeyPurple, Font = new Font("Segoe UI", 10) };
        _hkToggle = new HotkeyBox { Location = new Point(300, 123), Width = 80, Height = 24 };
        _hkToggle.HotkeyChanged += (_, _) => ShowToast($"✅ พร้อมใช้งาน: {_hkToggle.Text}");

        var smoke = TryMakeUiPictureBox("smoke", new Point(315, 155), new Size(65, 65));

        _cbEnableSound = new CheckBox { Text = "Sound", Location = new Point(230, 220), Checked = true, AutoSize = true };

        _btnToggle = new Label
        {
            Text = "OFF", Location = new Point(225, 175), Width = 85, Height = 40,
            TextAlign = ContentAlignment.MiddleCenter, BackColor = ColorPinkBg,
            ForeColor = ColorOffRed, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 11, FontStyle.Bold), Cursor = Cursors.Hand
        };
        _btnToggle.Click += (_, _) => ToggleMacro();

        _cbLockKey = new CheckBox { Text = "", Location = new Point(383, 125), Size = new Size(20, 20) };
        _tips.SetToolTip(_cbLockKey, "Lock (ล็อกไม่ให้แก้ไข hotkey)");
        _cbLockKey.CheckedChanged += (_, _) => _hkToggle.Enabled = !_cbLockKey.Checked;

        var controlsToAdd = new List<Control>
        {
            title, lblProfiles, _cbProfile, btnSave, lblWindow, _cbPid, btnRefresh, _lblStatus,
            _lblHpVal, _lblHpPer, _lblSpVal, _lblSpPer, lblSetHk, _hkToggle,
            _cbEnableSound, _btnToggle, _cbLockKey
        };
        if (hpIcon != null) controlsToAdd.Add(hpIcon);
        if (spIcon != null) controlsToAdd.Add(spIcon);
        if (smoke != null) controlsToAdd.Add(smoke);

        Controls.AddRange(controlsToAdd.ToArray());
        _topBarLockControls = controlsToAdd; // เก็บไว้ล็อกทีหลังตอนยังไม่ login (ดู SetLoginLock)
        smoke?.SendToBack(); // สมโค้ก (smoke.png) เป็นรูปตกแต่งพื้นหลัง ไม่บังคอนโทรลอื่น
    }

    /// <summary>โหลดไอคอนตกแต่ง UI จาก Icons\ ถ้ามี — คืน null เฉยๆ ถ้าไม่มีไฟล์ (ไม่ error, ไม่วาดอะไร)</summary>
    private static PictureBox? TryMakeUiPictureBox(string fileName, Point location, Size size)
    {
        var img = IconLoader.TryLoadUi(fileName);
        if (img == null) return null;
        return new PictureBox { Image = img, Location = location, Size = size, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
    }

    /// <summary>
    /// Offset สำหรับแปลงพิกัดสัมบูรณ์ของ AHK (x,y อ้างอิงหน้าต่างทั้งบาน) ให้เป็นพิกัดสัมพัทธ์ของ
    /// TabPage ใน WinForms (0,0 = มุมซ้ายบนของพื้นที่เนื้อหาแท็บ ไม่รวมแถบหัวข้อแท็บด้านบน)
    /// ต้นฉบับ: Tab := MyGui.Add("Tab", "x10 y240 w380 h430", ...)
    /// </summary>
    private const int TabOffsetX = 10;
    private const int TabOffsetY = 264; // 240 (ตำแหน่ง Tab) + ~24 (ความสูงแถบหัวข้อแท็บโดยประมาณ)
    private static Point TP(int ahkX, int ahkY) => new(ahkX - TabOffsetX, ahkY - TabOffsetY);

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Location = new Point(10, 240), Size = new Size(380, 430) };

        var tabMacro = new TabPage("MACRO");
        BuildMacroTab(tabMacro);
        var tabPot = new TabPage("AUTOPOT&&DBUFF");
        BuildAutopotTab(tabPot);
        var tabBuffs = new TabPage("BUFFS");
        BuildBuffsTab(tabBuffs);
        var tabTimer = new TabPage("TIMER");
        BuildTimerTab(tabTimer);
        var tabSpam = new TabPage("SPAM");
        BuildSpamTab(tabSpam);
        var tabProfiles = new TabPage("PROFILES");
        BuildProfilesTab(tabProfiles);

        // เก็บไว้ล็อกทีหลังตอนยังไม่ login (ดู SetLoginLock) — ทุกแท็บยกเว้น PROFILES
        _lockableTabPages = new List<TabPage> { tabMacro, tabPot, tabBuffs, tabTimer, tabSpam };
        // ในแท็บ PROFILES เอง ก็ต้องล็อกทุกอย่างยกเว้นช่องกรอกไอดี + ปุ่ม Login
        _profilesLockControls = tabProfiles.Controls.Cast<Control>()
            .Where(c => c != _edLicenseKey && c != _btnLogin).ToList();

        tabs.TabPages.AddRange(new[] { tabMacro, tabPot, tabBuffs, tabTimer, tabSpam, tabProfiles });
        return tabs;
    }

    private void BuildMacroTab(TabPage page)
    {
        page.Font = new Font("Tahoma", 8);
        page.Controls.Add(new Label { Text = "Key", Location = TP(30, 280), Width = 75, TextAlign = ContentAlignment.MiddleCenter });
        page.Controls.Add(new Label { Text = "Repeat", Location = TP(115, 280), Width = 55, TextAlign = ContentAlignment.MiddleCenter });
        page.Controls.Add(new Label { Text = "Delay(ms)", Location = TP(180, 280), Width = 70, TextAlign = ContentAlignment.MiddleCenter });
        page.Controls.Add(new Label { Text = "Click", Location = TP(260, 280), Width = 35, TextAlign = ContentAlignment.MiddleCenter });
        page.Controls.Add(new Label { Text = "Space", Location = TP(295, 280), Width = 40, TextAlign = ContentAlignment.MiddleCenter });

        for (int i = 1; i <= 6; i++)
        {
            int yy = 275 + i * 30; // เทียบเท่า yy := 275 + (idx * 30) ในต้นฉบับ
            var row = new MacroRow();

            row.KeyControl = new HotkeyBox { Location = TP(30, yy), Width = 75, Height = 22 };
            row.CountControl = new NumericUpDown { Location = TP(115, yy), Width = 55, Minimum = 1, Maximum = 999, Value = 1, TextAlign = HorizontalAlignment.Center };
            row.DelayControl = new TextBox { Location = TP(180, yy), Width = 70, Text = "100", TextAlign = HorizontalAlignment.Center };
            row.ClickCheck = new CheckBox { Location = TP(268, yy + 2), Size = new Size(16, 16) };
            row.SpaceCheck = new CheckBox { Location = TP(303, yy + 2), Size = new Size(16, 16) };

            page.Controls.AddRange(new Control[] { row.KeyControl, row.CountControl, row.DelayControl, row.ClickCheck, row.SpaceCheck });
            _macroRows.Add(row);
        }

        var grp = new GroupBox { Text = " ส่วนเสริม ", Location = TP(16, 490), Size = new Size(345, 60) };
        _cbAutoClick = new CheckBox { Location = new Point(11, 30), Size = new Size(20, 20) };
        var lblAutoClick = new Label { Text = "AutoClick", Location = new Point(37, 32), AutoSize = true };
        _cbAutoSpace = new CheckBox { Location = new Point(91, 30), Size = new Size(20, 20) };
        var lblAutoSpace = new Label { Text = "AutoSpace", Location = new Point(117, 32), AutoSize = true };
        _cbEmergencyEsc = new CheckBox { Location = new Point(176, 30), Size = new Size(20, 20) };
        var lblAutoEsc = new Label { Text = "AutoEsc", Location = new Point(197, 32), AutoSize = true };
        var btnMoveToEsc = new Button { Text = "MoveToEsc", Location = new Point(254, 30), Size = new Size(70, 20) };
        btnMoveToEsc.Click += (_, _) => MoveToEsc();
        grp.Controls.AddRange(new Control[] { _cbAutoClick, lblAutoClick, _cbAutoSpace, lblAutoSpace, _cbEmergencyEsc, lblAutoEsc, btnMoveToEsc });
        page.Controls.Add(grp);

        var btnMoveTo = new Button { Text = "Move\nCursor", Location = TP(25, 590), Size = new Size(100, 43) };
        btnMoveTo.Click += (_, _) => MoveMouseToGameCenter();
        var btnHideShow = new Button { Text = "Hide/Show\nGame", Location = TP(275, 590), Size = new Size(100, 43) };
        btnHideShow.Click += (_, _) => ToggleGameWindow();
        page.Controls.AddRange(new Control[] { btnMoveTo, btnHideShow });

        _engine.GetMacroRows = () => _macroRows;
        _engine.GetTimerRows = () => _timerRows;
        _engine.GetAutoClickEnabled = () => _cbAutoClick.Checked;
        _engine.GetAutoSpaceEnabled = () => _cbAutoSpace.Checked;
        _engine.GetEmergencyEscEnabled = () => _cbEmergencyEsc.Checked;
        _engine.MoveToEscAction = MoveToEscAuto;
        _engine.MoveMouseToGameCenterAction = MoveMouseToGameCenterAuto;
    }

    /// <summary>
    /// ตัวจริงของ MoveToEsc()/MoveMouseToGameCenter() ในต้นฉบับ AHK — ทำสิ่งเดียวกันแค่พิกัดต่างกัน
    /// (ratio ตำแหน่งเทียบกับขนาด client ของเกม 900x700) ทั้งคู่คลิกขวาจริงๆ (real cursor + real click
    /// ไม่ใช่ PostMessage เหมือน SendClickBackground) ที่จุดนั้น แล้วสลับกลับมาโปรแกรมและคืนตำแหน่งเมาส์เดิม
    /// </summary>
    private void PerformRealGameRightClick(double xRatio, double yRatio, bool isManual)
    {
        if (_engine.Memory is null)
        {
            if (isManual) MessageBox.Show("กรุณาเลือกหน้าต่างเกมก่อนครับ", "แจ้งเตือน");
            return;
        }

        IntPtr hwnd = WinHelper.FindMainWindowByPid(_engine.Memory.Pid);
        if (hwnd == IntPtr.Zero) return;

        bool originalPaused = _engine.Paused;
        _engine.Paused = true;
        try
        {
            // 1. จำพิกัดเมาส์ปัจจุบัน (จะคืนกลับตอนจบ)
            Native.GetCursorPos(out var startPt);

            // 2. คำนวณพิกัดเป้าหมายในเกมจาก client rect จริง (fixed ratio เหมือนต้นฉบับ)
            Native.GetClientRect(hwnd, out var rect);
            int winW = rect.Right - rect.Left, winH = rect.Bottom - rect.Top;
            var target = new Native.POINT { X = (int)(winW * xRatio), Y = (int)(winH * yRatio) };
            Native.ClientToScreen(hwnd, ref target);

            // 3. สลับไปเกม รอ active จริง แล้วคลิกขวาที่จุดนั้น (คลิกจริงระดับ OS)
            Native.SetForegroundWindow(hwnd);
            WinHelper.WaitForForeground(hwnd, 1000);
            Native.SetCursorPos(target.X, target.Y);
            Thread.Sleep(10);
            Native.RealRightClick();
            Thread.Sleep(10);

            // 4. สลับกลับมาโปรแกรม รอ active จริงก่อนค่อยคืนตำแหน่งเมาส์เดิม
            if (isManual)
            {
                Native.SetForegroundWindow(Handle);
                WinHelper.WaitForForeground(Handle, 1000);
                Native.SetCursorPos(startPt.X, startPt.Y);
            }
        }
        finally
        {
            _engine.Paused = originalPaused;
        }
    }

    /// <summary>เทียบเท่า MoveToEsc(isManual) ในต้นฉบับ — คลิกขวาตำแหน่ง (455/900, 462/700) ของ client เกม</summary>
    private void MoveToEsc() => PerformRealGameRightClick(455.0 / 900, 462.0 / 700, isManual: true);

    /// <summary>เวอร์ชัน isManual:false ของ MoveToEsc() — ไม่ดึงโฟกัสกลับมาที่โปรแกรมเองหลังคลิก
    /// (ปล่อยให้เกมอยู่ foreground ต่อ) ใช้เฉพาะตอนเรียกอัตโนมัติจาก EmergencyEscLogic ที่ต้องทำ
    /// ต่อด้วย MoveMouseToGameCenterAuto() ทันทีโดยไม่อยากให้โฟกัสกระโดดไปมาระหว่างสองสเต็ป</summary>
    private void MoveToEscAuto() => PerformRealGameRightClick(455.0 / 900, 462.0 / 700, isManual: false);

    private void BuildAutopotTab(TabPage page)
    {
        var hpPotIcon = TryMakeUiPictureBox("HPpot", TP(30, 275), new Size(58, 58));
        page.Controls.Add(new Label { Text = "HP < ", Location = TP(45, 350), AutoSize = true });
        _edHpLimit = new TextBox { Location = TP(90, 350), Width = 45, Height = 24, TextAlign = HorizontalAlignment.Center, Text = "80" };
        page.Controls.Add(new Label { Text = "%  Key:", Location = TP(140, 350), AutoSize = true });
        _hkHpKey = new HotkeyBox { Location = TP(185, 350), Width = 80, Height = 22 };
        page.Controls.AddRange(new Control[] { _edHpLimit, _hkHpKey });
        if (hpPotIcon != null) page.Controls.Add(hpPotIcon);

        var spPotIcon = TryMakeUiPictureBox("SPpot", TP(30, 380), new Size(58, 58));
        page.Controls.Add(new Label { Text = "SP < ", Location = TP(45, 450), AutoSize = true });
        _edSpLimit = new TextBox { Location = TP(90, 450), Width = 45, Height = 24, TextAlign = HorizontalAlignment.Center, Text = "40" };
        page.Controls.Add(new Label { Text = "%  Key:", Location = TP(140, 450), AutoSize = true });
        _hkSpKey = new HotkeyBox { Location = TP(185, 450), Width = 80, Height = 22 };
        page.Controls.AddRange(new Control[] { _edSpLimit, _hkSpKey });
        if (spPotIcon != null) page.Controls.Add(spPotIcon);

        var debuffIcon = TryMakeUiPictureBox("Debuff", TP(25, 500), new Size(65, 65));
        if (debuffIcon != null) page.Controls.Add(debuffIcon);

        // Loop DebuffIDs.Length: startX=55 startY=580 spacingX=65 spacingY=58 (5 คอลัมน์ต่อแถว)
        int startX = 55, startY = 580, spacingX = 65, spacingY = 58, cols = 5;
        for (int idx = 0; idx < BuffData.DebuffIDs.Length; idx++)
        {
            int col = idx % cols, row = idx / cols;
            int currX = startX + col * spacingX, currY = startY + row * spacingY;
            string baseName = BuffData.DebuffIDs[idx];

            object[] ids = baseName == "panacea" ? new object[] { 883, 885, 887, 886, 884, 34 } : new object[] { baseName };
            var entry = new BuffEntry { FileName = baseName, Ids = ids, IsDebuff = true, DisplayName = baseName };

            var icon = IconLoader.TryLoad(baseName);
            if (icon != null)
                page.Controls.Add(new PictureBox { Image = icon, Location = TP(currX, currY), Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom });
            else
                page.Controls.Add(new Label { Text = "X", Location = TP(currX, currY), Size = new Size(24, 24), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter });
            _tips.SetToolTip(page.Controls[^1], baseName);

            entry.KeyControl = new HotkeyBox { Location = TP(currX - 8, currY + 28), Width = 40, Height = 20 };
            page.Controls.Add(entry.KeyControl);
            _extraPots.Add(entry);
        }

        _engine.GetHpLimitPercent = () => double.TryParse(_edHpLimit.Text, out double v) ? v : 0;
        _engine.GetSpLimitPercent = () => double.TryParse(_edSpLimit.Text, out double v) ? v : 0;
        _engine.GetHpKey = () => _hkHpKey.HotkeyValue;
        _engine.GetSpKey = () => _hkSpKey.HotkeyValue;


        BuildCustomIconsSection(page, TP(20, 660).Y);
    }

    /// <summary>
    /// รูปไอคอนที่มีอยู่ใน Icons\ แต่ไม่ตรงกับ Buff ID ที่รู้จักใน BuffData เลย (ไอคอนไอเทม/รูปอื่นๆ
    /// ที่ผู้ใช้เพิ่มเข้ามาเอง) — โชว์เป็นแถวให้กำหนด ID ที่จะเช็คในเกม + ติ๊ก debuff (ถ้าใช่) + hotkey เอง
    /// </summary>
    private void BuildCustomIconsSection(TabPage page, int startY)
    {
        var knownIds = new HashSet<string>(BuffData.DebuffIDs, StringComparer.OrdinalIgnoreCase);
        foreach (var className in BuffData.ClassList)
        {
            foreach (var id in BuffData.GetListForClass(className))
            {
                string clean = Regex.Replace(id.ToString() ?? "", @"(\.png|_1)$", "");
                knownIds.Add(clean);
            }
        }

        var leftover = IconLoader.ListAvailableIconIds()
            .Where(id => !knownIds.Contains(id) && !id.Equals("logo", StringComparison.OrdinalIgnoreCase)
                         && !id.Equals("start", StringComparison.OrdinalIgnoreCase)
                         && !id.Equals("stop", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (leftover.Count == 0) return; // ไม่มีไอคอนเหลือ ไม่ต้องสร้างส่วนนี้



        // ใช้ค่าคงที่แทน page.Height เพราะตอนนี้ TabPage ยังไม่ถูกใส่เข้า TabControl
        // (ยังไม่ layout จริง) ค่า Height ที่อ่านได้ตอนนี้จะเป็นค่า default ที่ไม่ถูกต้อง
        const int tabContentHeight = 420;
        var scroll = new FlowLayoutPanel
        {
            Location = new Point(15, startY + 18),
            Size = new Size(345, Math.Max(60, tabContentHeight - startY - 18)),
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        page.Controls.Add(scroll);

        foreach (var fileName in leftover)
        {
            var row = new Panel { Size = new Size(325, 34), Margin = new Padding(1) };

            var icon = IconLoader.TryLoad(fileName);
            if (icon != null)
                row.Controls.Add(new PictureBox { Image = icon, Location = new Point(0, 1), Size = new Size(30, 30), SizeMode = PictureBoxSizeMode.Zoom });

            var idBox = new TextBox { Location = new Point(35, 7), Width = 90, Text = fileName };
            _tips.SetToolTip(idBox, "ID ของ buff/item ที่จะเช็คในเกม (ปกติเดาไว้ให้ตรงชื่อไฟล์ แก้ได้ถ้าไม่ตรง)");

            var debuffCheck = new CheckBox { Location = new Point(130, 9), Text = "debuff", AutoSize = true };
            _tips.SetToolTip(debuffCheck, "ติ๊กถ้าเป็นสถานะที่ต้องกด hotkey ตอน 'พบ' (เช่น ยาแก้พิษ) ปล่อยว่างถ้าเป็นบัฟที่ต้องคอยกดต่ออายุตอน 'หาย'");

            var keyBox = new HotkeyBox { Location = new Point(210, 5), Width = 70, Height = 22 };

            row.Controls.AddRange(new Control[] { idBox, debuffCheck, keyBox });
            scroll.Controls.Add(row);

            _customIconRows.Add(new CustomIconRow { FileName = fileName, IdBox = idBox, DebuffCheck = debuffCheck, KeyBox = keyBox });
        }
    }

    /// <summary>แปลง _customIconRows เป็น BuffEntry สดๆ ทุกครั้งที่เรียก (อ่านค่า textbox ปัจจุบันเสมอ)</summary>
    private List<BuffEntry> BuildCustomBuffEntries() => _customIconRows.Select(r => new BuffEntry
    {
        FileName = r.FileName,
        Ids = new object[] { int.TryParse(r.IdBox.Text, out int n) ? n : r.IdBox.Text },
        IsDebuff = r.DebuffCheck.Checked,
        DisplayName = r.FileName,
        Key = r.KeyBox.HotkeyValue
    }).ToList();

    private void BuildBuffsTab(TabPage page)
    {
        _classList = new ListBox { Location = TP(20, 265), Size = new Size(80, 356), Font = new Font("Segoe UI", 8) };
        _classList.Items.AddRange(BuffData.ClassList);
        _classList.SelectedIndexChanged += (_, _) => ShowBuffGroup(_classList.SelectedItem as string ?? "");
        page.Controls.Add(_classList);

        // เส้นแบ่งแนวตั้ง — เทียบเท่า Text "x115 y260 w1 h360 +Background334155" ในต้นฉบับ
        var divider = new Panel { Location = new Point(Math.Max(0, TP(115, 260).X), Math.Max(0, TP(115, 260).Y)), Size = new Size(1, 356), BackColor = Color.FromArgb(0x33, 0x45, 0x64) };
        page.Controls.Add(divider);

        _buffPanel = new FlowLayoutPanel { Location = TP(130, 265), Size = new Size(240, 356), AutoScroll = true, FlowDirection = FlowDirection.LeftToRight };
        page.Controls.Add(_buffPanel);

        if (_classList.Items.Count > 0) _classList.SelectedIndex = 0;

        _engine.GetBuffEntries = () => _extraPots.Concat(_buffRows).Concat(BuildCustomBuffEntries()).ToList();
    }

    private readonly Dictionary<string, List<BuffEntry>> _groupCache = new();

    private void ShowBuffGroup(string className)
    {
        if (className == "") return;
        _buffPanel.SuspendLayout();
        _buffPanel.Controls.Clear();

        if (!_groupCache.TryGetValue(className, out var entries))
        {
            entries = new List<BuffEntry>();
            foreach (var id in BuffData.GetListForClass(className))
            {
                string cleanId = Regex.Replace(id.ToString() ?? "", @"(\.png|_1)$", "");
                string displayName = BuffData.Names.TryGetValue(cleanId, out var n) ? n : $"Buff ID: {cleanId}";

                object[] finalIds = cleanId switch
                {
                    "FullProtection" => new object[] { 54, 55, 56, 57 },
                    "252" or "BubbleGumHE" => new object[] { 252 },
                    "shadow" or "undead" or "ghosting" => new object[] { 302 },
                    _ => new object[] { int.TryParse(cleanId, out int n2) ? n2 : cleanId }
                };

                entries.Add(new BuffEntry
                {
                    FileName = id.ToString() ?? "",
                    Ids = finalIds,
                    IsDebuff = cleanId is "622" or "panacea",
                    Group = className,
                    DisplayName = displayName
                });
            }
            _groupCache[className] = entries;
            _buffRows.AddRange(entries);
        }

        foreach (var entry in entries)
        {
            // ปรับให้ไอคอนบัฟในแท็บ BUFFS มีขนาด 24x24 เท่ากับไอคอนในแท็บ AUTOPOT&&DBUFF (เดิมยืดเป็น 36px)
            var cell = new Panel { Size = new Size(56, 54), Margin = new Padding(2) };
            var icon = IconLoader.TryLoad(entry.FileName);

            if (icon != null)
            {
                var pb = new PictureBox
                {
                    Image = icon, Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point((cell.Width - 24) / 2, 4)
                };
                _tips.SetToolTip(pb, entry.DisplayName);
                cell.Controls.Add(pb);
            }
            else
            {
                var lbl = new Label { Text = entry.DisplayName, Location = new Point(0, 4), Size = new Size(56, 24), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 7) };
                cell.Controls.Add(lbl);
            }

            entry.KeyControl = new HotkeyBox { Location = new Point(3, 32), Size = new Size(50, 20) };
            cell.Controls.Add(entry.KeyControl);
            _buffPanel.Controls.Add(cell);
        }
        _buffPanel.ResumeLayout();
    }

    private void BuildTimerTab(TabPage page)
    {
        page.Controls.Add(new Label { Text = "Key", Location = TP(40, 280), Width = 140, TextAlign = ContentAlignment.MiddleCenter });
        page.Controls.Add(new Label { Text = "Delay / Unit", Location = TP(200, 280), Width = 150, TextAlign = ContentAlignment.MiddleCenter });

        for (int idx = 1; idx <= 8; idx++)
        {
            int yy = 280 + idx * 38;
            var row = new MacroRow { IsTimer = true };

            row.KeyControl = new HotkeyBox { Location = TP(40, yy), Width = 140, Height = 28 };
            row.DelayControl = new TextBox { Location = TP(195, yy), Width = 70, Height = 28, TextAlign = HorizontalAlignment.Center };
            row.UnitCombo = new ComboBox { Location = TP(270, yy), Width = 65, DropDownStyle = ComboBoxStyle.DropDownList };
            row.UnitCombo.Items.AddRange(new object[] { "sec", "min" });
            row.UnitCombo.SelectedIndex = 0;

            page.Controls.AddRange(new Control[] { row.KeyControl, row.DelayControl, row.UnitCombo });
            _timerRows.Add(row);
        }
    }

    private void BuildSpamTab(TabPage page)
    {
        // cellW=40 cellH=45 startX=15 cols=9 startY=270 ในต้นฉบับ — checkbox เป็นแบบ 3-state (+0x6 / BS_AUTO3STATE)
        // คลิกครั้งละหนึ่งจะไล่ Off -> Click -> NoClick -> Off วนไปเรื่อยๆ (เทียบเท่า WinForms ThreeState CheckBox)
        int cellW = 40, cellH = 45, startX = 15, cols = 9, startY = 270;

        // F1-F9 (9 ตัวแรกของ KeyLabels) แถวเดียว
        for (int i = 1; i <= 9; i++)
        {
            int c = (i - 1) % cols;
            int cellX = startX + c * cellW, cellY = startY;
            AddSpamCell(page, KeyLabels[i - 1], TP(cellX, cellY), cellW, _normalSlotChecks, i);
        }

        // แถวตัวเลข 1-9
        int startYNum = startY + cellH;
        for (int i = 1; i <= NumericLabels.Length; i++)
        {
            int cellX = startX + (i - 1) * cellW, cellY = startYNum;
            AddSpamCell(page, NumericLabels[i - 1], TP(cellX, cellY), cellW, _numericSlotChecks, i);
        }

        // ตัวอักษรที่เหลือ (Q..M) ไล่ทีละ 9 ต่อแถว
        int startYLetters = startYNum + cellH;
        for (int i = 10; i <= KeyLabels.Length; i++)
        {
            int idx = i - 9;
            int r = (idx - 1) / cols, c = (idx - 1) % cols;
            int cellX = startX + c * cellW, cellY = startYLetters + r * cellH;
            AddSpamCell(page, KeyLabels[i - 1], TP(cellX, cellY), cellW, _normalSlotChecks, i);
        }

        int lettersRows = (int)Math.Ceiling((KeyLabels.Length - 9) / (double)cols); // = 3
        int groupY = startYLetters + lettersRows * cellH + 70;

        var grp = new GroupBox { Text = " Spam Mode ", Location = TP(15, groupY), Size = new Size(345, 95), Font = new Font("Tahoma", 8, FontStyle.Bold) };
        AddLegendRow(grp, "off", "ปิดใช้งาน (Deactivated)", 13, Color.Gray);
        AddLegendRow(grp, "click", "Spamปุ่ม + คลิกซ้าย", 33, Color.Green);
        AddLegendRow(grp, "noclick", "Spamปุ่มอย่างเดียว (ไม่คลิก)", 53, Color.Black);
        page.Controls.Add(grp);
    }

    private void AddSpamCell(TabPage page, string label, Point location, int cellW, Dictionary<int, CheckBox> targetDict, int index)
    {
        // ตั้ง AutoSize=false + Height ตายตัวไว้ชัดเจน เพราะ Label.AutoSize (ค่า default = true) จะขยาย
        // สูงเกินพอดีจนทับ checkbox ด้านล่างถ้าปล่อยให้คำนวณเอง
        page.Controls.Add(new Label
        {
            Text = label, Location = location, Size = new Size(cellW, 13), AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 7, FontStyle.Bold)
        });

        var cb = new CheckBox
        {
            Location = new Point(location.X + (cellW - 14) / 2, location.Y + 18), Size = new Size(14, 14),
            ThreeState = true, Appearance = Appearance.Normal
        };
        cb.CheckStateChanged += (_, _) => _spamSystem.SetSlot(label, (SpamMode)(int)cb.CheckState);
        page.Controls.Add(cb);
        targetDict[index] = cb;
    }

    private void AddLegendRow(GroupBox grp, string iconName, string text, int y, Color textColor)
    {
        var icon = TryMakeUiPictureBox(iconName, new Point(15, y), new Size(14, 14));
        if (icon != null) grp.Controls.Add(icon);
        grp.Controls.Add(new Label { Text = text, Location = new Point(40, y - 2), Width = 290, ForeColor = textColor, Font = new Font("Tahoma", 8, FontStyle.Bold) });
    }

    private void BuildProfilesTab(TabPage page)
    {
        page.Controls.Add(new Label { Text = "Login System", Location = TP(25, 270), AutoSize = true, ForeColor = ColorTitleBlue, Font = new Font("Segoe UI", 10) });
        page.Controls.Add(new Label { Text = "ID :", Location = TP(25, 295), AutoSize = true });
        _edLicenseKey = new TextBox { Location = TP(25, 320), Width = 260, Height = 26, Text = LicenseManager.GetSavedKey() };
        _btnLogin = new Button { Text = "Login", Location = TP(295, 319), Width = 80, Height = 28 };
        _btnLogin.Click += async (_, _) => await CheckLicenseAsync(_edLicenseKey.Text);
        page.Controls.AddRange(new Control[] { _edLicenseKey, _btnLogin });

        var divider = new Panel { Location = TP(10, 355), Size = new Size(360, 2), BackColor = Color.Gainsboro };
        page.Controls.Add(divider);

        page.Controls.Add(new Label { Text = "Profile Manager", Location = TP(25, 370), AutoSize = true, ForeColor = ColorTitleBlue, Font = new Font("Segoe UI", 10) });
        _edProfileName = new TextBox { Location = TP(25, 395), Width = 260, Height = 26 };
        var btnCreate = new Button { Text = "Create", Location = TP(295, 394), Width = 85, Height = 28 };
        btnCreate.Click += (_, _) => CreateNewProfile(_edProfileName.Text);
        page.Controls.AddRange(new Control[] { _edProfileName, btnCreate });

        _lbProfiles = new ListBox { Location = TP(25, 440), Size = new Size(260, 110) };
        _lbProfiles.SelectedIndexChanged += (_, _) => { if (_lbProfiles.SelectedItem is string s) _edProfileName.Text = s; };
        var btnRename = new Button { Text = "Rename", Location = TP(295, 440), Width = 85, Height = 34 };
        btnRename.Click += (_, _) => RenameProfile(_edProfileName.Text);
        var btnRemove = new Button { Text = "Remove", Location = TP(295, 478), Width = 85, Height = 34 };
        btnRemove.Click += (_, _) => RemoveProfile();
        var btnCopy = new Button { Text = "Copy", Location = TP(295, 516), Width = 85, Height = 34 };
        btnCopy.Click += (_, _) => CopyProfile();
        page.Controls.AddRange(new Control[] { _lbProfiles, btnRename, btnRemove, btnCopy });

        var btnAddServer = new Button { Text = "+ Add New Server", Location = TP(25, 575), Width = 170, Height = 40 };
        btnAddServer.Click += (_, _) => ShowAddServerDialog();
        var btnClearServers = new Button { Text = "🗑 Clear All Servers", Location = TP(205, 575), Width = 170, Height = 40 };
        btnClearServers.Click += (_, _) => ClearServers();
        page.Controls.AddRange(new Control[] { btnAddServer, btnClearServers });

        RefreshProfileLists();
    }

    // =========================================================================
    // Lifecycle / status polling — เทียบเท่า UpdateStatus()/WatchProcessStatus() timers
    // =========================================================================

    private async void OnFormLoad()
    {
        ProfileManager.EnsureProfileDir();
        RefreshProfileLists();
        RefreshPidList();

        string defaultPath = Path.Combine(ProfileManager.ProfileDir, "Default.json");
        if (File.Exists(defaultPath)) LoadProfile("Default");
        else SaveProfile("Default");

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += (_, _) => { UpdateStatusUi(); WatchProcessStatus(); };
        _uiTimer.Start();

        // ล็อกทุกอย่างไว้ก่อนจนกว่าจะ login ผ่าน แล้วลอง auto-login ด้วยไอดีที่เคยเซฟไว้ (ถ้ามี)
        SetLoginLock(true);
        string savedKey = LicenseManager.GetSavedKey();
        if (!string.IsNullOrEmpty(savedKey))
        {
            ShowToast("⏳ กำลัง auto-login...");
            await CheckLicenseAsync(savedKey, isAutoLogin: true);
        }
    }

    private string _lastKnownCharName = "";

    private void UpdateStatusUi()
    {
        if (!_engine.IsSelected || _engine.Memory is null)
        {
            _lblHpVal.Text = "0 / 0";
            _lblSpVal.Text = "0 / 0";
            UpdateTrayStatus("", 0, 0);
            return;
        }

        var (curHp, maxHp) = _engine.ReadHp();
        var (curSp, maxSp) = _engine.ReadSp();
        int pHp = maxHp > 0 ? (int)Math.Round(curHp / (double)maxHp * 100) : 0;
        int pSp = maxSp > 0 ? (int)Math.Round(curSp / (double)maxSp * 100) : 0;

        _lblHpVal.Text = $"{curHp} / {maxHp}";
        _lblSpVal.Text = $"{curSp} / {maxSp}";
        _lblHpPer.Text = $"( {pHp}% )";
        _lblSpPer.Text = $"( {pSp}% )";

        // อ่านชื่อตัวละครซ้ำทุกรอบ (แทนที่จะอ่านครั้งเดียวตอนกด Confirm) เพราะถ้าเลือกตัวละครใหม่
        // ในหน้าเลือกตัวหลังเชื่อมต่อ memory ไว้แล้ว ชื่อที่อยู่ address เดิมจะเปลี่ยนตามไปด้วย
        string rawName = _engine.ReadCharacterName(_nameAddr);
        string charName = string.IsNullOrEmpty(rawName) || rawName == "Unknown" ? "Connected" : rawName;
        if (charName != _lastKnownCharName)
        {
            _lastKnownCharName = charName;
            _lblStatus.Text = $"✔ {charName}";
        }

        UpdateTrayStatus(charName, pHp, pSp);
    }

    private void WatchProcessStatus()
    {
        if (_engine.IsSelected && _engine.Memory != null && !WinHelper.ProcessExists(_engine.Memory.Pid))
            ResetSelection();
    }

    private void ShowToast(string message)
    {
        // ตามที่ขอ: ไม่เอาข้อความสถานะไปแปะไว้ที่ title bar ข้างชื่อโปรแกรมแล้ว
        // (เก็บ method ไว้เพราะมีจุดเรียกใช้เยอะ แต่ตอนนี้ไม่ทำอะไรกับ UI ที่มองเห็นได้)
        System.Diagnostics.Debug.WriteLine($"[SherLock] {message}");
    }

    /// <summary>สร้าง tray icon + context menu (Show/Exit) — เทียบเท่า TraySetIcon()/A_IconTip ในต้นฉบับ</summary>
    private void BuildTrayIcon()
    {
        _iconActive = IconLoader.TryLoadIcon("Active");
        _iconInactive = IconLoader.TryLoadIcon("Inactive");

        var menu = new ContextMenuStrip();
        var miShow = menu.Items.Add("เปิดโปรแกรม");
        miShow.Click += (_, _) => RestoreFromTray();
        menu.Items.Add(new ToolStripSeparator());
        var miExit = menu.Items.Add("ออกจากโปรแกรม");
        miExit.Click += (_, _) => { _trayIcon.Visible = false; Application.Exit(); };

        _trayIcon = new NotifyIcon
        {
            Icon = _iconInactive ?? Icon ?? SystemIcons.Application,
            Text = "SherLock Macro 2",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>เทียบเท่า UpdateStatus() ในต้นฉบับ — อัปเดตไอคอน tray ตาม running/paused + tooltip
    /// แสดงชื่อตัวละคร/สถานะ/HP/SP — เรียกจาก _uiTimer ทุก 500ms เหมือน HP/SP label ปกติ</summary>
    private void UpdateTrayStatus(string charName, int pHp, int pSp)
    {
        bool connected = _engine.IsSelected && _engine.Memory != null;
        string text;

        if (connected)
        {
            _trayIcon.Icon = _engine.IsRunning ? (_iconActive ?? _trayIcon.Icon) : (_iconInactive ?? _trayIcon.Icon);
            string runStatus = _engine.IsRunning ? "RUNNING" : "PAUSED";
            text = $"Name: {charName}\nStatus: {runStatus}\nHP: {pHp}% | SP: {pSp}%";
        }
        else
        {
            _trayIcon.Icon = _iconInactive ?? _trayIcon.Icon;
            text = "STATUS: PLEASE SELECT WINDOW GAME";
        }

        if (text.Length > 63) text = text[..63]; // ข้อจำกัดของ NotifyIcon.Text ใน WinForms
        if (text != _lastTrayText)
        {
            _lastTrayText = text;
            _trayIcon.Text = text;
        }
    }

    // =========================================================================
    // Window/PID selection — เทียบเท่า RefreshPIDList()/ConfirmSelection()
    // =========================================================================
  private void RefreshPidList()
    {
        ResetSelection();
        var list = new List<string> { "" };
        var targetExeList = SettingsManager.Load().Servers.Select(s => s.Exe).ToList();

        if (targetExeList.Count > 0)
        {
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    string pName = proc.ProcessName + ".exe";
                    if (!targetExeList.Contains(pName, StringComparer.OrdinalIgnoreCase)) continue;
                    string entry = $"[{pName} {proc.Id}]";
                    if (!list.Contains(entry)) list.Add(entry);
                }
                catch { /* process อาจปิดไปแล้วระหว่าง enum */ }
            }
        }

        _cbPid.Items.Clear();
        _cbPid.Items.AddRange(list.ToArray());
        _cbPid.SelectedIndex = 0;
    }

    private void OnPidSelected()
    {
        if (_engine.IsRunning)
        {
            MessageBox.Show("กรุณาหยุดการทำงาน (STOP)", "แจ้งเตือน");
            return;
        }
        if (_cbPid.Text == "")
        {
            ResetSelection();
            return;
        }

        var match = Regex.Match(_cbPid.Text, @"\[(.+)\s(\d+)\]");
        if (!match.Success) return;

        string selectedExe = match.Groups[1].Value;
        int selectedPid = int.Parse(match.Groups[2].Value);

        _spamSystem.ClearAll();

        var server = SettingsManager.Load().Servers
            .FirstOrDefault(s => s.Exe.Equals(selectedExe, StringComparison.OrdinalIgnoreCase));
        bool found = server != null;

        if (found)
        {
            _hpAddr = Convert.ToInt64(server!.HpAddr, server.HpAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
            _nameAddr = Convert.ToInt64(server.NameAddr, server.NameAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
        }

        if (!found)
        {
            MessageBox.Show("ไม่พบข้อมูล Address สำหรับ EXE นี้", "Error");
            ResetSelection();
            return;
        }

        if (!_engine.Connect(selectedPid, _hpAddr))
        {
            MessageBox.Show("ไม่สามารถเชื่อมต่อได้ (ลองรันด้วย Admin)", "Error");
            ResetSelection();
            return;
        }

        string rawName = _engine.ReadCharacterName(_nameAddr);
        string charName = string.IsNullOrEmpty(rawName) || rawName == "Unknown" ? "Connected" : rawName;
        _lastKnownCharName = charName;
        _lblStatus.Text = $"✔ {charName}";
        _lblStatus.BackColor = Color.FromArgb(0xD9, 0xFF, 0xD9);
        _lblStatus.ForeColor = Color.FromArgb(0, 0x5A, 0x9E);
    }

    private void ResetSelection()
    {
        _spamSystem.ClearAll();
        _engine.ResetSelection();
        ApplyToggleVisual(running: false);
        _lastKnownCharName = "";
        _isGameVisible = null; // รีเซ็ต ให้เช็คจาก window จริงใหม่ตอนเชื่อมต่อเกมตัวถัดไป
        _lblStatus.Text = "DISCONNECTED";
        _lblStatus.BackColor = Color.FromArgb(0xFF, 0xD9, 0xD9);
        _lblStatus.ForeColor = Color.FromArgb(0x80, 0, 0);
        _lblHpVal.Text = "0 / 0"; _lblSpVal.Text = "0 / 0";
        _lblHpPer.Text = "( 0% )"; _lblSpPer.Text = "( 0% )";
    }

    // =========================================================================
    // Toggle macro on/off — เทียบเท่า ToggleMacro()/StopMacro()
    // =========================================================================

    /// <summary>ตัวดัก global hotkey จริงของ ToggleMacro — เทียบ vkCode ที่กดกับ _hkToggle.HotkeyValue
    /// (ก่อนหน้านี้ไม่มีจุดนี้เลย ทำให้ hotkey ไม่ทำงาน มีแต่ปุ่มบน GUI ที่ผูก ToggleMacro() ไว้ตรงๆ)</summary>
    private void OnGlobalToggleKeyDown(int vk)
    {
        if (_hkToggle.Focused) return; // กำลังตั้งค่า hotkey ใหม่อยู่ ไม่ต้อง toggle
        if (string.IsNullOrEmpty(_hkToggle.HotkeyValue) || _hkToggle.HotkeyValue == "None") return;
        if (KeySender.GetVirtualKey(_hkToggle.HotkeyValue) != vk) return;
        if (_toggleKeyPhysicallyDown) return; // กันคีย์ค้างส่ง WM_KEYDOWN ซ้ำๆ (auto-repeat)

        _toggleKeyPhysicallyDown = true;
        ToggleMacro();
    }

    private void OnGlobalToggleKeyUp(int vk)
    {
        if (KeySender.GetVirtualKey(_hkToggle.HotkeyValue) == vk)
            _toggleKeyPhysicallyDown = false;
    }

    private void ToggleMacro()
    {
        if (_engine.Memory is null)
        {
            ShowToast("⚠️ กด Confirm เลือก Process ก่อนครับ!");
            return;
        }

        if (!_engine.IsRunning)
        {
            _engine.Start();
            ApplyToggleVisual(running: true);
            _hkToggle.Enabled = false;
            if (_cbEnableSound.Checked) SoundLoader.Play("start");
        }
        else
        {
            _engine.Stop();
            ApplyToggleVisual(running: false);
            _hkToggle.Enabled = true;
            if (_cbEnableSound.Checked) SoundLoader.Play("stop");
        }
    }

    /// <summary>ใช้ไอคอน start.png/stop.png จาก Icons\ ถ้ามี ไม่มีก็ fallback เป็นสีพื้น+ตัวหนังสือเหมือนเดิม</summary>
    private void ApplyToggleVisual(bool running)
    {
        var icon = IconLoader.TryLoadUi(running ? "stop" : "start"); // running อยู่ -> โชว์ไอคอน stop ให้กด
        if (icon != null)
        {
            _btnToggle.Image = icon;
            _btnToggle.ImageAlign = ContentAlignment.MiddleCenter;
            _btnToggle.Text = "";
        }
        else
        {
            _btnToggle.Text = running ? "ON" : "OFF";
        }
        _btnToggle.BackColor = running ? Color.FromArgb(0xD9, 0xFF, 0xD9) : Color.FromArgb(0xFF, 0xD9, 0xD9);
        _btnToggle.ForeColor = running ? Color.FromArgb(0xA6, 0, 0) : Color.FromArgb(0x80, 0, 0);
    }

    // =========================================================================
    // Move-to-esc / move-cursor / hide-show — เทียบเท่าฟังก์ชันชื่อเดียวกันในต้นฉบับ
    // =========================================================================

    /// <summary>เทียบเท่า MoveMouseToGameCenter(isManual) ในต้นฉบับ — คลิกขวาตำแหน่ง (447/900, 352/700)</summary>
    private void MoveMouseToGameCenter() => PerformRealGameRightClick(449.0 / 900, 352.0 / 700, isManual: true);

    /// <summary>เวอร์ชัน isManual:false ของ MoveMouseToGameCenter() — ไม่ดึงโฟกัสกลับมาที่โปรแกรมเองหลังคลิก
    /// ใช้คู่กับ MoveToEscAuto() ตอนเรียกอัตโนมัติจาก EmergencyEscLogic</summary>
    private void MoveMouseToGameCenterAuto() => PerformRealGameRightClick(449.0 / 900, 352.0 / 700, isManual: false);

    private System.Threading.Timer? _gdiWatchTimer;

    /// <summary>
    /// null = ยังไม่เคยกดปุ่มนี้เลยสักครั้ง (เช็คจาก window จริงครั้งแรกเท่านั้น)
    /// หลังจากกดครั้งแรกไปแล้ว จะใช้ค่านี้เป็นความจริงหลักเสมอ ไม่เช็ค GetWindowLong ซ้ำอีก
    /// เพราะเกมบางตัว (โดยเฉพาะ DirectX/fullscreen) รายงานค่า WS_VISIBLE ไม่ตรงกับที่ตาเห็นจริง
    /// หลัง hide/show สลับไปมาหลายรอบ ทำให้ปุ่มดูเหมือน "ติด" อยู่ที่โหมดแสดงผลตลอด
    /// </summary>
    private bool? _isGameVisible;

    /// <summary>
    /// เทียบเท่า ToggleGameWindow()/WatchGDIWindow() ในต้นฉบับ — ซ่อน/แสดงหน้าต่างเกมจริง และคอยยิง
    /// WM_CLOSE ให้หน้าต่าง "GDI+ Window" ย่อยของเกมทุก 50ms ระหว่างที่เกมแสดงผลอยู่ (เกมตัวนี้มีหน้าต่าง
    /// GDI+ ลูกที่ชอบเด้งขึ้นมาทับเอง ต้องคอยปิดมันไปเรื่อยๆ)
    /// </summary>
    private void ToggleGameWindow()
    {
        if (_engine.Memory is null) { MessageBox.Show("กรุณาเลือกหน้าต่างเกมก่อนครับ", "แจ้งเตือน"); return; }
        var hwnd = WinHelper.FindMainWindowByPid(_engine.Memory.Pid);
        if (hwnd == IntPtr.Zero) return;

        // ครั้งแรกที่กด (ยังไม่มี state ของเราเอง) ค่อยเช็คจาก window จริง — หลังจากนั้นเชื่อ state
        // ที่เราเก็บเองเป็นหลักเสมอ (ดูเหตุผลที่ comment ของ field ด้านบน)
        bool isVisible = _isGameVisible ?? (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_VISIBLE) != 0;

        if (isVisible)
        {
            // ตอนซ่อนหน้าต่าง
            _isGameVisible = false;
            Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE);
            Native.ShowWindowAsync(hwnd, Native.SW_HIDE);
        }
        else
        {
            // ตอนแสดงหน้าต่าง — ใช้ ShowWindowAsync (แนะนำสำหรับหน้าต่างข้ามโปรเซส/เธรดโดย Microsoft
            // เอง ปลอดภัยกว่า ShowWindow ธรรมดาตรงที่ไม่ค้างรอถ้า thread ปลายทางไม่ตอบสนอง) +
            // ยิง WM_SYSCOMMAND/SC_RESTORE สำรองไว้ด้วย เพราะเกมบางตัว (โดยเฉพาะ DirectX exclusive
            // fullscreen) ไม่ตอบสนองกับ ShowWindow(SW_SHOW/SW_RESTORE) เพียงอย่างเดียวหลังโดนซ่อนไปแล้ว
            _isGameVisible = true;
            Native.PostMessage(hwnd, Native.WM_SYSCOMMAND, (IntPtr)Native.SC_RESTORE, IntPtr.Zero);
            Native.ShowWindowAsync(hwnd, Native.SW_RESTORE);
            Native.ShowWindowAsync(hwnd, Native.SW_SHOW);
            Native.SetForegroundWindow(hwnd);
        }
    }

    private void StartGdiWatchTimer()
    {
        _gdiWatchTimer = new System.Threading.Timer(_ =>
        {
            if (_isGameVisible != true || _engine.Memory is null) return;
            var gdiHwnd = WinHelper.FindWindowByTitlePrefixAndPid("GDI+ Window", _engine.Memory.Pid);
            if (gdiHwnd != IntPtr.Zero)
                Native.PostMessage(gdiHwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }, null, 0, 50);
    }

    // =========================================================================
    // Profiles — เทียบเท่า SaveProfile()/LoadProfile()/CreateNewProfileCustom() ฯลฯ
    // =========================================================================

    private void RefreshProfileLists()
    {
        var list = ProfileManager.GetProfileList();
        _cbProfile.Items.Clear();
        _cbProfile.Items.AddRange(list.ToArray());
        if (_cbProfile.Items.Count > 0) _cbProfile.SelectedIndex = 0;

        _lbProfiles.Items.Clear();
        _lbProfiles.Items.AddRange(list.ToArray());
    }

    private void SaveProfile(string name)
    {
        name = string.IsNullOrWhiteSpace(name) || name == "Profile" ? "Default" : name.Trim();

        var data = new ProfileData
        {
            ToggleHotkey = _hkToggle.HotkeyValue,
            HpLimit = _edHpLimit.Text,
            HpKey = _hkHpKey.HotkeyValue,
            SpLimit = _edSpLimit.Text,
            SpKey = _hkSpKey.HotkeyValue,
            AutoClick = _cbAutoClick.Checked,
            AutoSpace = _cbAutoSpace.Checked,
            AutoEsc = _cbEmergencyEsc.Checked,
        };

      foreach (var row in _timerRows)
        {
            data.TimerRows.Add(new RowProfileData
            {
                Key = row.KeyControl?.HotkeyValue ?? "",
                Delay = int.TryParse(row.DelayControl?.Text, out int td) ? td : 100,
                Unit = row.UnitCombo?.SelectedItem as string ?? "sec",
            });
        }

        for (int i = 0; i < _extraPots.Count; i++)
            data.ExtraPotKeys[i + 1] = _extraPots[i].KeyControl?.HotkeyValue ?? "";
        for (int i = 0; i < _buffRows.Count; i++)
            data.BuffRowKeys[i + 1] = _buffRows[i].KeyControl?.HotkeyValue ?? "";

        foreach (var row in _macroRows)
        {
            data.Rows.Add(new RowProfileData
            {
                Key = row.KeyControl?.HotkeyValue ?? "",
                Count = (int)(row.CountControl?.Value ?? 1),
                Delay = int.TryParse(row.DelayControl?.Text, out int d) ? d : 100,
                Click = row.ClickCheck?.Checked ?? false,
                Space = row.SpaceCheck?.Checked ?? false,
            });
        }

        foreach (var (i, cb) in _normalSlotChecks)
            data.NormalSlots[i] = (int)cb.CheckState;
        foreach (var (i, cb) in _numericSlotChecks)
            data.NumericSlots[i] = (int)cb.CheckState;

        foreach (var r in _customIconRows)
            data.CustomIcons[r.FileName] = $"{r.IdBox.Text}|{r.KeyBox.HotkeyValue}|{(r.DebuffCheck.Checked ? 1 : 0)}";

        ProfileManager.Save(name, data);
        ShowToast($"💾 บันทึกโปรไฟล์สำเร็จ: {name}");
    }

    private void LoadProfile(string name)
    {
        var data = ProfileManager.Load(name);

        _hkToggle.SetValue(data.ToggleHotkey);
        _edHpLimit.Text = data.HpLimit;
        _hkHpKey.SetValue(data.HpKey);
        _edSpLimit.Text = data.SpLimit;
        _hkSpKey.SetValue(data.SpKey);
        _cbAutoClick.Checked = data.AutoClick;
        _cbAutoSpace.Checked = data.AutoSpace;
        _cbAutoSpace.Checked = data.AutoSpace;

        for (int i = 0; i < _macroRows.Count && i < data.Rows.Count; i++)
        {
            var r = data.Rows[i];
            var row = _macroRows[i];
            row.KeyControl?.SetValue(r.Key);
            if (row.CountControl != null) row.CountControl.Value = Math.Clamp(r.Count, 1, 999);
            if (row.DelayControl != null) row.DelayControl.Text = r.Delay.ToString();
            if (row.ClickCheck != null) row.ClickCheck.Checked = r.Click;
            if (row.SpaceCheck != null) row.SpaceCheck.Checked = r.Space;
        }

         for (int i = 0; i < _timerRows.Count && i < data.TimerRows.Count; i++)
        {
            var r = data.TimerRows[i];
            var row = _timerRows[i];
            row.KeyControl?.SetValue(r.Key);
            if (row.DelayControl != null) row.DelayControl.Text = r.Delay.ToString();
            if (row.UnitCombo != null) row.UnitCombo.SelectedItem = r.Unit;
        }

        foreach (var (i, key) in data.ExtraPotKeys)
            if (i - 1 < _extraPots.Count) _extraPots[i - 1].KeyControl?.SetValue(key);
        foreach (var (i, key) in data.BuffRowKeys)
            if (i - 1 < _buffRows.Count) _buffRows[i - 1].KeyControl?.SetValue(key);

        foreach (var (i, v) in data.NormalSlots)
            if (_normalSlotChecks.TryGetValue(i, out var cb)) cb.CheckState = (CheckState)v;
        foreach (var (i, v) in data.NumericSlots)
            if (_numericSlotChecks.TryGetValue(i, out var cb)) cb.CheckState = (CheckState)v;

        foreach (var r in _customIconRows)
        {
            if (!data.CustomIcons.TryGetValue(r.FileName, out string? packed)) continue;
            var parts = packed.Split('|');
            if (parts.Length >= 1) r.IdBox.Text = parts[0];
            if (parts.Length >= 2) r.KeyBox.SetValue(parts[1]);
            if (parts.Length >= 3) r.DebuffCheck.Checked = parts[2] == "1";
        }

        ShowToast($"📄 โหลดโปรไฟล์สำเร็จ: {name}");
    }

    private void CreateNewProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "Profile")
        {
            ShowToast("⚠️ กรุณาพิมพ์ชื่อโปรไฟล์");
            return;
        }
        SaveProfile(name);
        RefreshProfileLists();
        _edProfileName.Text = "";
        ShowToast($"✅ สร้างโปรไฟล์ '{name}' เรียบร้อย");
    }

    private void RenameProfile(string newName)
    {
        string oldName = _lbProfiles.Text;
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == "Profile")
        {
            ShowToast("⚠️ เลือกโปรไฟล์และใส่ชื่อใหม่ก่อน");
            return;
        }
        string oldPath = Path.Combine(ProfileManager.ProfileDir, $"{oldName}.json");
        string newPath = Path.Combine(ProfileManager.ProfileDir, $"{newName}.json");
        if (File.Exists(newPath)) return;

        try
        {
            File.Move(oldPath, newPath);
            RefreshProfileLists();
            ShowToast($"✏️ Renamed: {oldName} -> {newName}");
        }
        catch { /* เงียบเหมือนต้นฉบับถ้าย้ายไม่สำเร็จ */ }
    }

    private void RemoveProfile()
    {
        string selected = _lbProfiles.Text;
        if (string.IsNullOrEmpty(selected) || selected == "Profile") return;
        if (MessageBox.Show($"ต้องการลบโปรไฟล์ '{selected}' หรือไม่?", "ยืนยัน", MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;

        try
        {
            File.Delete(Path.Combine(ProfileManager.ProfileDir, $"{selected}.json"));
            RefreshProfileLists();
            ShowToast($"🗑 Removed: {selected}");
        }
        catch { }
    }

    private void CopyProfile()
    {
        string oldName = _lbProfiles.Text;
        if (string.IsNullOrEmpty(oldName)) { ShowToast("⚠️ กรุณาเลือกโปรไฟล์จากในรายการก่อน"); return; }

        string baseName = $"{oldName}-copy";
        string newName = baseName;
        int num = 1;
        while (File.Exists(Path.Combine(ProfileManager.ProfileDir, $"{newName}.json")))
            newName = $"{baseName}{num++}";

        try
        {
            File.Copy(Path.Combine(ProfileManager.ProfileDir, $"{oldName}.json"),
                      Path.Combine(ProfileManager.ProfileDir, $"{newName}.json"));
            RefreshProfileLists();
            ShowToast($"📋 ก๊อปปี้จาก {oldName} เป็น {newName} สำเร็จ");
        }
        catch { ShowToast("❌ ไม่สามารถก๊อปปี้ไฟล์ได้"); }
    }

    // =========================================================================
    // Server list management — เทียบเท่า ShowAddServerGui()/SaveNewServer()/ClearServers()
    // =========================================================================

    private void ShowAddServerDialog()
    {
        using var dlg = new AddServerForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SettingsManager.AddServer(dlg.ExeName, dlg.HpAddressHex, dlg.NameAddressHex);
            MessageBox.Show("บันทึกเรียบร้อย", "Success");
            RefreshPidList();
        }
    }

    private void ClearServers()
    {
        if (MessageBox.Show("ลบ Server ทั้งหมด?", "ยืนยัน", MessageBoxButtons.YesNo) == DialogResult.Yes)
        {
            SettingsManager.ClearServers();
            RefreshPidList();
        }
    }

    /// <summary>ล็อกทุกอย่างยกเว้นแท็บ PROFILES + ช่องกรอกไอดี/ปุ่ม Login จนกว่าจะ login ผ่าน</summary>
    private void SetLoginLock(bool locked)
    {
        _isLoggedIn = !locked;
        foreach (var c in _topBarLockControls) c.Enabled = !locked;
        foreach (var p in _lockableTabPages) p.Enabled = !locked;
        foreach (var c in _profilesLockControls) c.Enabled = !locked;
    }

    private async Task CheckLicenseAsync(string inputKey, bool isAutoLogin = false)
    {
        if (string.IsNullOrEmpty(inputKey)) return;

        var tokens = LicenseManager.GetHardwareTokens();
        if (!isAutoLogin) ShowToast("⏳ กำลังตรวจสอบสิทธิ์กับเซิร์ฟเวอร์..");
        var status = await LicenseManager.PerformFullVerifyAsync(inputKey, tokens);

        if (status == LicenseStatus.Matched)
        {
            File.WriteAllText(LicenseManager.GetLicenseFilePath(), inputKey);
            _btnLogin.Text = "Activated";
            _btnLogin.Enabled = false;
            _edLicenseKey.Enabled = false;
            SetLoginLock(false);
            if (!isAutoLogin) MessageBox.Show("✅ ยืนยันสิทธิ์เรียบร้อย! ยินดีต้อนรับ", "Success");
        }
        else
        {
            SetLoginLock(true); // ยังไม่ผ่าน (หรือ auto-login ไม่ผ่าน) — ล็อกไว้เหมือนเดิม
            if (isAutoLogin) return; // auto-login ไม่ผ่านไม่ต้องเด้ง error ใส่หน้า ให้ผู้ใช้กด Login เองแทน

            string msg = status switch
            {
                LicenseStatus.UsedByOtherDevice => "ID นี้ถูกใช้โดยเครื่องอื่นแล้ว",
                LicenseStatus.NotFound => "ไม่พบ ID นี้ในระบบ",
                _ => "การเชื่อมต่อล้มเหลว หรือสิทธิ์ถูกปฏิเสธ"
            };
            MessageBox.Show($"❌{msg}", "Error");
        }
    }
}
