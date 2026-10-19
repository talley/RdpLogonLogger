using System.Diagnostics.Eventing.Reader;
using System.Threading.Channels;
using System.Xml.Linq;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace RdpLogonLogger;

public sealed class RdpLogonWorker(ILogger<RdpLogonWorker> log, ExcelStore store, AppPaths paths) : BackgroundService
{
    // Remote Desktop Services session log. Enabled by default, no audit policy needed.
    //   21 = session logon, 25 = reconnection, 24 = disconnected (window closed), 23 = logoff
    private const string LogName = "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational";

    private readonly Channel<SessionEvent> _queue =
        Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions { SingleReader = true });
    private EventLogWatcher? _watcher;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();   // don't block host startup
        try
        {
            if (WindowsServiceHelpers.IsWindowsService())
                ServiceRecovery.Configure(log);   // sets the Recovery tab (restart after 1 minute)

            EnsureLogEnabled();

            long lastId = ReadState();
            log.LogInformation("Service started. Last processed record id = {Id} ({Mode})",
                lastId, lastId == 0 ? "first run: reading existing history" : "catching up on events missed while stopped");

            string query = $"*[System[(EventID=21 or EventID=23 or EventID=24 or EventID=25) and EventRecordID>{lastId}]]";
            _watcher = new EventLogWatcher(new EventLogQuery(LogName, PathType.LogName, query), null, readExistingEvents: true);
            _watcher.EventRecordWritten += OnEvent;
            _watcher.Enabled = true;
            log.LogInformation("Watching '{Log}'", LogName);

            await foreach (var ev in _queue.Reader.ReadAllAsync(ct))
                await ProcessAsync(ev, ct);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            FailFast("Worker crashed", ex);
        }
    }

    // ---- receiving events -------------------------------------------------------------------

    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null)
        {
            FailFast($"Event log watcher error: {e.EventException?.Message}", e.EventException);
            return;
        }

        try
        {
            using var rec = e.EventRecord;
            var ev = Parse(rec);
            if (ev is null)
            {
                log.LogWarning("RECEIVED event {EventId} record {Rec} but could not parse it", rec.Id, rec.RecordId);
                return;
            }
            log.LogInformation("RECEIVED record={Rec} event={EventId}({Kind}) session={Session} user='{User}' address='{Addr}' time={Time:yyyy-MM-dd HH:mm:ss}",
                ev.RecordId, ev.EventId, ev.Kind, ev.SessionId, ev.User, ev.Address, ev.Time);
            _queue.Writer.TryWrite(ev);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to read an event from the watcher");
        }
    }

    private static SessionEvent? Parse(EventRecord rec)
    {
        SessionEventKind? kind = rec.Id switch
        {
            21 => SessionEventKind.Logon,
            25 => SessionEventKind.Reconnect,
            24 => SessionEventKind.Disconnect,
            23 => SessionEventKind.Logoff,
            _  => null
        };
        if (kind is null) return null;

        var doc = XDocument.Parse(rec.ToXml());
        var data = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "UserData") ?? doc.Root!;
        string Get(string name) =>
            data.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? "";

        int.TryParse(Get("SessionID"), out int sid);
        return new SessionEvent(rec.RecordId ?? 0, rec.Id, kind.Value, sid, Get("User"), Get("Address"),
                                rec.TimeCreated ?? DateTime.Now);   // TimeCreated is already local time
    }

    // ---- processing (one at a time, in order) -----------------------------------------------

    private async Task ProcessAsync(SessionEvent ev, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                log.LogInformation("RESULT record={Rec}: {Result}", ev.RecordId, Handle(ev));
                SaveState(ev.RecordId);
                return;
            }
            catch (IOException ex)
            {
                log.LogWarning("Excel file is locked/in use (attempt {N}): {Msg}. Close it in Excel; retrying in 10s.", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not process record {Rec} ({Kind}); skipping it", ev.RecordId, ev.Kind);
                SaveState(ev.RecordId);
                return;
            }
        }
    }

    private string Handle(SessionEvent ev)
    {
        switch (ev.Kind)
        {
            case SessionEventKind.Logon:
            case SessionEventKind.Reconnect:
                if (string.IsNullOrEmpty(ev.Address) || ev.Address.Equals("LOCAL", StringComparison.OrdinalIgnoreCase))
                    return $"IGNORED - local console {ev.Kind} (address '{ev.Address}'), not RDP";

                // Only ask Windows for the client name if the session is live right now.
                string client = ev.Time > DateTime.Now.AddMinutes(-2) ? Wts.GetClientName(ev.SessionId) : "";
                bool added = store.AddConnection(ev, Environment.MachineName, client);
                return added
                    ? $"ADDED {ev.Kind}: {ev.User} from {client} ({ev.Address}) session {ev.SessionId}"
                    : $"SKIPPED - connection already recorded (session {ev.SessionId} at {ev.Time:HH:mm:ss})";

            default: // Disconnect, Logoff
                return $"{ev.Kind.ToString().ToUpperInvariant()} session {ev.SessionId}: {store.EndConnection(ev)}";
        }
    }

    // ---- helpers ----------------------------------------------------------------------------

    private void EnsureLogEnabled()
    {
        try
        {
            using var cfg = new EventLogConfiguration(LogName);
            if (cfg.IsEnabled) return;
            log.LogWarning("'{Log}' was disabled - enabling it", LogName);
            cfg.IsEnabled = true;
            cfg.SaveChanges();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not check/enable log '{Log}'", LogName);
        }
    }

    private long ReadState()
    {
        try
        {
            return File.Exists(paths.StatePath) && long.TryParse(File.ReadAllText(paths.StatePath).Trim(), out var v) ? v : 0;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not read {Path}; starting from 0", paths.StatePath);
            return 0;
        }
    }

    private void SaveState(long recordId)
    {
        try { File.WriteAllText(paths.StatePath, recordId.ToString()); }
        catch (Exception ex) { log.LogWarning(ex, "Could not save state file"); }
    }

    /// Log, flush, and terminate abruptly (exit code 1). Windows sees an unexpected exit and applies the
    /// Recovery settings (restart after 1 minute). Events raised while we are down are picked up from state.txt.
    private void FailFast(string reason, Exception? ex)
    {
        log.LogCritical(ex, "FATAL: {Reason}. Exiting with code 1 so Windows restarts the service.", reason);
        Serilog.Log.CloseAndFlush();
        Environment.Exit(1);
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
