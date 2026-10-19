namespace RdpLogonLogger;

public enum SessionEventKind { Logon, Reconnect, Disconnect, Logoff }

public sealed record SessionEvent(
    long RecordId, int EventId, SessionEventKind Kind,
    int SessionId, string User, string Address, DateTime Time);
