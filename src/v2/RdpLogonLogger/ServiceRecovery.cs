using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RdpLogonLogger;

/// Configures this service's own "Recovery" tab in services.msc when the service starts:
///   First / Second / Subsequent failures = Restart the Service
///   Reset fail count after = 0 days
///   Restart service after  = 1 minute
///   "Enable actions for stops with errors" = unchecked
internal static class ServiceRecovery
{
    private const string ServiceName = "RdpLogonLogger";
    private const uint RestartDelayMs = 60_000;          // 1 minute
    private const uint ResetPeriodSeconds = 0;           // 0 days

    private const int SC_MANAGER_CONNECT = 0x0001;
    private const int SERVICE_QUERY_CONFIG = 0x0001;
    private const int SERVICE_CHANGE_CONFIG = 0x0002;
    private const int SERVICE_START = 0x0010;            // required by Windows when setting a "restart" failure action
    private const int SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const int SERVICE_CONFIG_FAILURE_ACTIONS_FLAG = 4;
    private const int SC_ACTION_RESTART = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION { public int Type; public uint Delay; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public uint dwResetPeriod;
        public IntPtr lpRebootMsg;
        public IntPtr lpCommand;
        public uint cActions;
        public IntPtr lpsaActions;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, int access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scm, string name, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, IntPtr info);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public static void Configure(ILogger log)
    {
        IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero, actions = IntPtr.Zero, info = IntPtr.Zero, flag = IntPtr.Zero;
        try
        {
            scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed");

            svc = OpenServiceW(scm, ServiceName, SERVICE_QUERY_CONFIG | SERVICE_CHANGE_CONFIG | SERVICE_START);
            if (svc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService('{ServiceName}') failed");

            // Three actions: first failure, second failure, subsequent failures - all "restart after 1 minute".
            int size = Marshal.SizeOf<SC_ACTION>();
            actions = Marshal.AllocHGlobal(size * 3);
            for (int i = 0; i < 3; i++)
                Marshal.StructureToPtr(new SC_ACTION { Type = SC_ACTION_RESTART, Delay = RestartDelayMs }, actions + i * size, false);

            info = Marshal.AllocHGlobal(Marshal.SizeOf<SERVICE_FAILURE_ACTIONS>());
            Marshal.StructureToPtr(new SERVICE_FAILURE_ACTIONS
            {
                dwResetPeriod = ResetPeriodSeconds,
                lpRebootMsg = IntPtr.Zero,
                lpCommand = IntPtr.Zero,
                cActions = 3,
                lpsaActions = actions
            }, info, false);

            if (!ChangeServiceConfig2W(svc, SERVICE_CONFIG_FAILURE_ACTIONS, info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting failure actions failed");

            // "Enable actions for stops with errors" -> unchecked
            flag = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(flag, 0);
            if (!ChangeServiceConfig2W(svc, SERVICE_CONFIG_FAILURE_ACTIONS_FLAG, flag))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting failure-actions flag failed");

            log.LogInformation("Recovery options set: restart the service on 1st/2nd/subsequent failure, after 1 minute, reset count after 0 days");
        }
        catch (Exception ex)
        {
            // Never stop the service just because recovery settings couldn't be applied.
            log.LogWarning(ex, "Could not configure service recovery options");
        }
        finally
        {
            if (flag != IntPtr.Zero) Marshal.FreeHGlobal(flag);
            if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            if (actions != IntPtr.Zero) Marshal.FreeHGlobal(actions);
            if (svc != IntPtr.Zero) CloseServiceHandle(svc);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }
}
