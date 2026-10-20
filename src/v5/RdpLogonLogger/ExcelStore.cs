using System.Globalization;
using ClosedXML.Excel;

namespace RdpLogonLogger;

/// One row per RDP connection (a connect or reconnect, until the window is closed or the user signs out).
public sealed class ExcelStore(AppPaths paths, ILogger<ExcelStore> log)
{
    private readonly object _lock = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string Active = "Active";
    private const string Fmt = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] Headers =
    {
        "ServerName", "ClientName", "ClientIP", "UserName", "SessionId", "ConnectType",
        "LoginDate", "LoginTime", "LogoutDate", "LogoutTime", "Duration", "EndReason", "SessionLoggedOff"
    };

    private const int CServer = 1, CClient = 2, CIp = 3, CUser = 4, CSession = 5, CType = 6,
                      CInDate = 7, CInTime = 8, COutDate = 9, COutTime = 10, CDur = 11,
                      CReason = 12, CLoggedOff = 13;

    /// Adds a row for a new connection. Returns false if this exact connection is already recorded.
    public bool AddConnection(SessionEvent ev, string server, string client)
    {
        lock (_lock)
        {
            using var wb = Open();
            var ws = wb.Worksheet(1);
            string sid = ev.SessionId.ToString(Inv);
            string d = ev.Time.ToString("yyyy-MM-dd", Inv), t = ev.Time.ToString("HH:mm:ss", Inv);
            int last = LastRow(ws);

            for (int r = last; r >= 2; r--)
            {
                if (ws.Cell(r, CSession).GetString() != sid) continue;
                if (ws.Cell(r, CInDate).GetString() == d && ws.Cell(r, CInTime).GetString() == t)
                    return false;                                   // already recorded (e.g. after a restart)
                if (ws.Cell(r, CReason).GetString() == Active)      // earlier connection never got a disconnect
                    ws.Cell(r, CReason).Value = "Unknown (no disconnect event seen)";
            }

            int row = last + 1;
            ws.Cell(row, CServer).Value = server;
            ws.Cell(row, CClient).Value = client;
            ws.Cell(row, CIp).Value = ev.Address;
            ws.Cell(row, CUser).Value = ev.User;
            ws.Cell(row, CSession).Value = ev.SessionId;
            ws.Cell(row, CType).Value = ev.Kind == SessionEventKind.Reconnect ? "Reconnect" : "New logon";
            ws.Cell(row, CInDate).Value = d;
            ws.Cell(row, CInTime).Value = t;
            ws.Cell(row, CReason).Value = Active;
            Save(wb);
            return true;
        }
    }

    /// Handles a disconnect (window closed) or logoff (signed out). Returns a description for the log.
    public string EndConnection(SessionEvent ev)
    {
        lock (_lock)
        {
            if (!File.Exists(paths.ExcelPath)) return "no workbook yet - nothing to update";

            using var wb = Open();
            var ws = wb.Worksheet(1);
            string sid = ev.SessionId.ToString(Inv);

            int row = 0;
            for (int r = LastRow(ws); r >= 2; r--)
                if (ws.Cell(r, CSession).GetString() == sid) { row = r; break; }
            if (row == 0) return $"no row for session {sid} (not an RDP connection we recorded, e.g. console session)";

            bool isLogoff = ev.Kind == SessionEventKind.Logoff;
            string reason = ws.Cell(row, CReason).GetString();

            if (reason == Active)
            {
                CloseRow(ws, row, ev.Time, isLogoff ? "Logged off" : "Disconnected");
                if (isLogoff) ws.Cell(row, CLoggedOff).Value = ev.Time.ToString(Fmt, Inv);
                Save(wb);
                return $"row {row} closed as '{ws.Cell(row, CReason).GetString()}'";
            }

            if (isLogoff && ws.Cell(row, CLoggedOff).GetString() == "")
            {
                ws.Cell(row, CLoggedOff).Value = ev.Time.ToString(Fmt, Inv);
                // Sign-out logged a moment after the disconnect = the user actually signed out.
                if (reason == "Disconnected" &&
                    TryStamp(ws.Cell(row, COutDate).GetString(), ws.Cell(row, COutTime).GetString(), out var outAt) &&
                    (ev.Time - outAt).TotalSeconds <= 10)
                    ws.Cell(row, CReason).Value = "Logged off";
                Save(wb);
                return $"row {row} stamped with SessionLoggedOff";
            }

            return $"row {row} (session {sid}) already closed as '{reason}'; nothing to do";
        }
    }

    private static void CloseRow(IXLWorksheet ws, int row, DateTime when, string reason)
    {
        ws.Cell(row, COutDate).Value = when.ToString("yyyy-MM-dd", Inv);
        ws.Cell(row, COutTime).Value = when.ToString("HH:mm:ss", Inv);
        ws.Cell(row, CReason).Value = reason;
        if (TryStamp(ws.Cell(row, CInDate).GetString(), ws.Cell(row, CInTime).GetString(), out var login))
        {
            var d = when - login;
            ws.Cell(row, CDur).Value = $"{(int)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
        }
    }

    private static bool TryStamp(string date, string time, out DateTime result) =>
        DateTime.TryParseExact($"{date} {time}", Fmt, Inv, DateTimeStyles.None, out result);

    private static int LastRow(IXLWorksheet ws) => ws.LastRowUsed()?.RowNumber() ?? 1;

    private XLWorkbook Open()
    {
        string file = paths.ExcelPath;
        if (File.Exists(file))
        {
            var existing = new XLWorkbook(file);
            if (existing.Worksheets.Count > 0 && existing.Worksheet(1).Cell(1, CSession).GetString() == "SessionId")
                return existing;

            existing.Dispose();   // workbook from an older version of this service: keep it, start fresh
            string old = Path.Combine(paths.DataDir, $"rdp_sessions.old-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
            File.Move(file, old);
            log.LogWarning("Existing workbook had an old layout; renamed to {Old}", old);
        }

        var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("RdpSessions");
        for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        return wb;
    }

    private void Save(XLWorkbook wb)
    {
        wb.Worksheet(1).Columns().AdjustToContents();
        wb.SaveAs(paths.ExcelPath);
    }
}
