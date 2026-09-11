namespace SherlockMacro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // ต้อง register ก่อน ไม่งั้น Encoding.GetEncoding(874) ใน GameMemory.ReadString จะ throw
        // (เทียบเท่าการที่ AHK อ่าน string ภาษาไทยจาก memory ได้ตรงๆ โดยไม่ต้องตั้งค่าอะไรเพิ่ม)
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        ApplicationConfiguration.Initialize();

        // เทียบเท่า ProcessSetPriority("High") ในต้นฉบับ AHK
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.High;
        }
        catch { /* ไม่ critical ถ้าตั้งไม่สำเร็จ */ }

        Application.Run(new MainForm());
    }
}
