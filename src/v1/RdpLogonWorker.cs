using System.Diagnostics.Eventing.Reader;
using System.Threading.Channels;

namespace RdpLogonLogger;

public sealed class RdpLogonWorker(ILogger<RdpLogonWorker> log, ExcelStore store) : BackgroundService
{
    // 4624 (LogonType 10 = RDP) -> login
    // 4778 -> reconnect, 4779 -> disconnect (window closed, session still alive)
    // 4634 / 4647 -> logoff
    private const string Query = """
        <QueryList><Query Id="0" Path="Security">
          <Select Path="Security">*[System[(EventID=4624)]] and *[EventData[Data[@Name='LogonType']='10']]</Select>
          <Select Path="Security">*[System[(EventID=4634 or EventID=4647 or EventID=4778 or EventID=4779)]]</Select>
        </Query></QueryList>
        """;

    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>();
    private EventLogWatcher? _watcher;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _watcher = new EventLogWatcher(new EventLogQuery("Security", PathType.LogName, Query));
        _watcher.EventRecordWritten += OnEvent;
        _watcher.Enabled = true;
        log.LogInformation("Listening for RDP logon/logoff events...");

        // Single consumer: writes happen in order, and are retried if the xlsx is open in Excel.
        try
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(ct))
            {
                while (!ct.IsCancellationRequested)
                {
                    try { work(); break; }
                    catch (IOException ex)
                    {
                        log.LogWarning("Excel file is locked ({Msg}). Retrying in 10s. Close it in Excel.", ex.Message);
                        await Task.Delay(TimeSpan.FromSeconds(10), ct);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to write event");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnEvent(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord is null)
        {
            log.LogWarning("Event watcher error: {Err}", e.EventException?.Message);
            return;
        }

        try
        {
            using var rec = e.EventRecord;
            var p = rec.Properties;
            DateTime when = (rec.TimeCreated ?? DateTime.Now).ToLocalTime();

            switch (rec.Id)
            {
                case 4624:
                {
                    string id     = Id(p[7].Value);             // TargetLogonId
                    string user   = p[5].Value?.ToString() ?? "";
                    string domain = p[6].Value?.ToString() ?? "";
                    string client = p[11].Value?.ToString() ?? "";
                    string ip     = p[18].Value?.ToString() ?? "";
                    _queue.Writer.TryWrite(() =>
                    {
                        store.AddLogon(Environment.MachineName, client, ip, $"{domain}\\{user}", id, when);
                        log.LogInformation("LOGIN  {Domain}\\{User} from {Client} ({Ip})", domain, user, client, ip);
                    });
                    break;
                }
                case 4634:  // TargetLogonId = index 3
                case 4647:  // SubjectLogonId = index 3
                {
                    string id = Id(p[3].Value);
                    _queue.Writer.TryWrite(() =>
                    {
                        if (store.MarkLogout(id, when)) log.LogInformation("LOGOUT session {Id}", id);
                    });
                    break;
                }
                case 4779:  // LogonID = index 2
                {
                    string id = Id(p[2].Value);
                    _queue.Writer.TryWrite(() =>
                    {
                        if (store.MarkDisconnected(id, when)) log.LogInformation("DISCONNECT session {Id}", id);
                    });
                    break;
                }
                case 4778:
                {
                    string id = Id(p[2].Value);
                    _queue.Writer.TryWrite(() =>
                    {
                        if (store.MarkReconnected(id)) log.LogInformation("RECONNECT session {Id}", id);
                    });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to parse event");
        }
    }

    private static string Id(object? v) => v switch
    {
        null   => "",
        long l => $"0x{l:x}",
        ulong u => $"0x{u:x}",
        _      => v.ToString()!.ToLowerInvariant()
    };

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
