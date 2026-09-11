using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SherlockMacro;

public enum LicenseStatus { Matched, NotFound, UsedByOtherDevice, Error }

/// <summary>
/// เทียบเท่า CheckLicense()/PerformFullVerify()/GetHardwareTokens()/SetRegistryToken() ในต้นฉบับ AHK
/// ผูก license กับ MAC address ของเครื่อง แล้วยืนยันกับ backend เดียวกัน (Supabase REST)
/// </summary>
public static class LicenseManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    // กำหนดค่า URL และ API Key แบบข้อความปกติ
    private static readonly string _0xU_Data = "https://jagxgmlxxtycaigbxtnk.supabase.co";
    private static readonly string _0xK_Data = "sb_publishable_27I5OxxUWnKtKYUvhgcA3Q__oWdN6wl";

    public static string GetLicenseFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SherLock_LicenseKey.txt");

    public static string GetSavedKey()
    {
        var path = GetLicenseFilePath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }

    /// <summary>เทียบเท่า GetHardwareTokens() — MAC address ของ adapter ที่ online + token เดิมใน Registry</summary>
    public static (string Mac, string RegToken) GetHardwareTokens()
    {
        string mac = "None";
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                  n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        if (nic != null)
            mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

        string regToken = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SherLock");
            regToken = key?.GetValue("Token") as string ?? "";
        }
        catch { /* ไม่มีก็ข้าม เหมือนต้นฉบับ */ }

        return (mac, regToken);
    }

    public static void SetRegistryToken(string token)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\SherLock");
        key.SetValue("Token", token);
    }

    /// <summary>เทียบเท่า PerformFullVerify(Key, Tokens)</summary>
    public static async Task<LicenseStatus> PerformFullVerifyAsync(string licenseKey, (string Mac, string RegToken) tokens)
    {
        if (string.IsNullOrEmpty(_0xU_Data))
            return LicenseStatus.Error; // ยังไม่ได้ตั้งค่า URL

        string baseUrl = _0xU_Data;
        string apiKey = _0xK_Data;

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/licenses?license_key=eq.{licenseKey}&select=*");
            req.Headers.Add("apikey", apiKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var resp = await Http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();

            if (body == "[]") return LicenseStatus.NotFound;

            if (body.Contains(tokens.Mac))
            {
                SetRegistryToken(licenseKey);
                return LicenseStatus.Matched;
            }
            if (!string.IsNullOrEmpty(tokens.RegToken) && body.Contains(tokens.RegToken))
                return LicenseStatus.Matched;

            if (body.Contains("\"mac_address\":null") || body.Contains("\"mac_address\":\"None\""))
            {
                var patch = new HttpRequestMessage(HttpMethod.Patch,
                    $"{baseUrl}/rest/v1/licenses?license_key=eq.{licenseKey}");
                patch.Headers.Add("apikey", apiKey);
                patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                var payload = JsonSerializer.Serialize(new { mac_address = tokens.Mac, reg_token = licenseKey });
                patch.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var patchResp = await Http.SendAsync(patch);
                if (patchResp.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.OK)
                {
                    SetRegistryToken(licenseKey);
                    return LicenseStatus.Matched;
                }
            }

            return LicenseStatus.UsedByOtherDevice;
        }
        catch
        {
            return LicenseStatus.Error;
        }
    }
}