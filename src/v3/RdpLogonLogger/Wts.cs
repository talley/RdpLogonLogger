using System.Runtime.InteropServices;

namespace RdpLogonLogger;

/// Asks Windows for the name of the computer an RDP session is connected from.
internal static class Wts
{
    private const int WTSClientName = 10;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr hServer, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    public static string GetClientName(int sessionId)
    {
        IntPtr buf = IntPtr.Zero;
        try
        {
            return WTSQuerySessionInformationW(IntPtr.Zero, sessionId, WTSClientName, out buf, out _)
                ? Marshal.PtrToStringUni(buf)?.Trim() ?? ""
                : "";
        }
        catch { return ""; }
        finally { if (buf != IntPtr.Zero) WTSFreeMemory(buf); }
    }
}
