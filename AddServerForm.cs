using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SherlockMacro;

public sealed class AddServerForm : Form
{
    private readonly ComboBox _cbExe;
    private readonly TextBox _edHp;
    private readonly TextBox _edName;
    private readonly Button _btnAutoScan;

    public string ExeName => ExtractExeName(_cbExe.Text.Trim());
    public string HpAddressHex => NormalizeHex(_edHp.Text);
    public string NameAddressHex => NormalizeHex(_edName.Text);

    public AddServerForm()
    {
        Text = "Add New Server Config";
        ClientSize = new Size(240, 275);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(new Label { Text = "เลือกหน้าต่างเกม", Location = new Point(65, 15), Width = 350 });

        // ComboBox แสดงเฉพาะ process ที่เปิดอยู่บน taskbar ที่อยู่ใน Settings
        _cbExe = new ComboBox { Location = new Point(15, 40), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        LoadRunningProcesses();
        Controls.Add(_cbExe);

        Controls.Add(new Label { Text = "HP Address (0x):", Location = new Point(15, 80), Width = 230 });
        _edHp = new TextBox { Location = new Point(15, 105), Width = 200, Height = 24 };
        Controls.Add(_edHp);

        // ปุ่ม Auto Scan อยู่ฝั่งขวาของ HP Address
        _btnAutoScan = new Button { Text = "Auto Scan", Location = new Point(15, 220), Width = 100, Height = 30 };
        _btnAutoScan.Click += BtnAutoScan_Click;
        Controls.Add(_btnAutoScan);

        Controls.Add(new Label { Text = "Name Address (0x):", Location = new Point(15, 145), Width = 350 });
        _edName = new TextBox { Location = new Point(15, 170), Width = 200, Height = 24 };
        Controls.Add(_edName);

        // ปุ่ม Save Config พร้อมผูก Event และเพิ่มเข้า Controls
        var btnSave = new Button { Text = "💾 Save Config", Location = new Point(115, 220), Width = 100, Height = 30 };
        btnSave.Click += (s, e) => OnSave();
        Controls.Add(btnSave);
    }

    private void LoadRunningProcesses()
    {
        _cbExe.Items.Clear();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                string pName = proc.ProcessName + ".exe";
                
                string entry = $"[{pName} {proc.Id}]";
                if (!_cbExe.Items.Contains(entry))
                    _cbExe.Items.Add(entry);
            }
            catch { }
        }
        
        // ถ้ายังไม่เจอ ลองดึงแบบไม่เช็ค MainWindowHandle เผื่อเกมซ่อนหน้าต่าง
        if (_cbExe.Items.Count == 0)
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string pName = proc.ProcessName + ".exe";
                    string entry = $"[{pName} {proc.Id}]";
                    if (!_cbExe.Items.Contains(entry))
                        _cbExe.Items.Add(entry);
                }
                catch { }
            }
        }

        if (_cbExe.Items.Count > 0) _cbExe.SelectedIndex = 0;
    }
    private void BtnAutoScan_Click(object? sender, EventArgs e)
    {
        string selectedText = _cbExe.Text;
        if (string.IsNullOrEmpty(selectedText))
        {
            MessageBox.Show("กรุณาเลือกหน้าต่างเกมก่อนสแกน", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ดึง PID/ชื่อ exe ออกมาจาก string รูปแบบ [process.exe PID]
        try
        {
            int pid = ExtractPid(selectedText);
            if (pid == 0) return;

            _btnAutoScan.Enabled = false;
            _btnAutoScan.Text = "Scanning...";
            Cursor = Cursors.WaitCursor;

            using var gameMem = new GameMemory(pid);
            if (!gameMem.IsValid)
            {
                MessageBox.Show("ไม่สามารถเปิด Process เพื่ออ่านหน่วยความจำได้", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var (hpAddr, nameAddr) = gameMem.FindAddresses(out var diag);

            if (hpAddr != 0) _edHp.Text = hpAddr.ToString("X");
            if (nameAddr != 0) _edName.Text = nameAddr.ToString("X");

            if (hpAddr == 0 && nameAddr == 0)
            {
                string diagText = $"Region ที่เจอทั้งหมด: {diag.TotalRegionsSeen}\n" +
                                   $"Region ที่สแกนได้: {diag.RegionsScanned}\n" +
                                   $"อ่านไม่ผ่าน: {diag.FailedReads}\n" +
                                   $"รวม byte ที่สแกน: {diag.BytesScanned:N0}";
                MessageBox.Show(
                    $"ไม่พบ Address ที่ตรงกับแพทเทิร์น กรุณากรอกเอง\n\n{diagText}",
                    "Result", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"เกิดข้อผิดพลาด: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnAutoScan.Enabled = true;
            _btnAutoScan.Text = "Auto Scan";
            Cursor = Cursors.Default;
        }
    }

    private void OnSave()
    {
        if (_cbExe.Text == "" || _edHp.Text == "" || _edName.Text == "")
        {
            MessageBox.Show("กรุณากรอกข้อมูลให้ครบ", "ข้อมูลไม่ครบ");
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string ExtractExeName(string bracketedText)
    {
        // รูปแบบ "[CustomSS2-RO.exe 11632]" -> ตัดเอาแค่ "CustomSS2-RO.exe"
        int startIdx = bracketedText.LastIndexOf(' ') + 1;
        int openIdx = bracketedText.IndexOf('[');
        if (startIdx <= 0 || openIdx < 0 || startIdx - 2 <= openIdx) return bracketedText;
        return bracketedText.Substring(openIdx + 1, startIdx - openIdx - 2).Trim();
    }

    private static int ExtractPid(string bracketedText)
    {
        int startIdx = bracketedText.LastIndexOf(' ') + 1;
        int endIdx = bracketedText.LastIndexOf(']');
        if (startIdx <= 0 || endIdx <= startIdx) return 0;
        return int.TryParse(bracketedText.Substring(startIdx, endIdx - startIdx), out int pid) ? pid : 0;
    }

    private static string NormalizeHex(string v) => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v : "0x" + v;
}