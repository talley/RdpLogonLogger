using ClosedXML.Excel;

namespace RdpLogonLogger;

/// One row per RDP session. Rows are matched by LogonId, so state survives service restarts.
public sealed class ExcelStore(IConfiguration config)
{
    private readonly object _lock = new();
    private readonly string _path = config["Output:ExcelPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "RdpLogonLogger", "rdp_sessions.xlsx");

    private static readonly string[] Headers =
    {
        "ServerName", "ClientName", "ClientIP", "UserName",
        "LoginDate", "LoginTime", "LogoutDate", "LogoutTime",
        "Duration", "LastDisconnect", "LogonId", "Status"
    };

    private const int ColLoginDate = 5, ColLoginTime = 6, ColLogoutDate = 7, ColLogoutTime = 8,
                      ColDuration = 9, ColDisconnect = 10, ColLogonId = 11, ColStatus = 12;

    public void AddLogon(string server, string client, string ip, string user, string logonId, DateTime when)
    {
        lock (_lock)
        {
            using var wb = Open();
            var ws = wb.Worksheet(1);
            int row = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
            ws.Cell(row, 1).Value = server;
            ws.Cell(row, 2).Value = client;
            ws.Cell(row, 3).Value = ip;
            ws.Cell(row, 4).Value = user;
            ws.Cell(row, ColLoginDate).Value = when.ToString("yyyy-MM-dd");
            ws.Cell(row, ColLoginTime).Value = when.ToString("HH:mm:ss");
            ws.Cell(row, ColLogonId).Value = logonId;
            ws.Cell(row, ColStatus).Value = "Active";
            Save(wb);
        }
    }

    public bool MarkDisconnected(string logonId, DateTime when) =>
        Update(logonId, (ws, r) =>
        {
            ws.Cell(r, ColDisconnect).Value = when.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(r, ColStatus).Value = "Disconnected";
        });

    public bool MarkReconnected(string logonId) =>
        Update(logonId, (ws, r) => ws.Cell(r, ColStatus).Value = "Active");

    public bool MarkLogout(string logonId, DateTime when) =>
        Update(logonId, (ws, r) =>
        {
            ws.Cell(r, ColLogoutDate).Value = when.ToString("yyyy-MM-dd");
            ws.Cell(r, ColLogoutTime).Value = when.ToString("HH:mm:ss");
            ws.Cell(r, ColStatus).Value = "Logged out";
            if (DateTime.TryParse($"{ws.Cell(r, ColLoginDate).GetString()} {ws.Cell(r, ColLoginTime).GetString()}", out var login))
            {
                var d = when - login;
                ws.Cell(r, ColDuration).Value = $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
            }
        });

    /// Applies the change to the still-open row for this LogonId. Returns false if we aren't tracking it.
    private bool Update(string logonId, Action<IXLWorksheet, int> change)
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return false;
            using var wb = Open();
            var ws = wb.Worksheet(1);
            int last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = last; r >= 2; r--)
            {
                if (ws.Cell(r, ColLogonId).GetString() == logonId && ws.Cell(r, ColLogoutDate).GetString() == "")
                {
                    change(ws, r);
                    Save(wb);
                    return true;
                }
            }
            return false;
        }
    }

    private XLWorkbook Open()
    {
        if (File.Exists(_path)) return new XLWorkbook(_path);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("RdpSessions");
        for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        return wb;
    }

    private void Save(XLWorkbook wb)
    {
        var ws = wb.Worksheet(1);
        ws.Columns().AdjustToContents();
        wb.SaveAs(_path);
    }
}
