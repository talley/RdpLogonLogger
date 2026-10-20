namespace RdpLogonLogger;

/// Everything the service writes lives under one folder (default C:\ProgramData\RdpLogonLogger).
public sealed class AppPaths
{
    public string DataDir { get; }
    public string ExcelPath => Path.Combine(DataDir, "rdp_sessions.xlsx");
    public string LogsDir   => Path.Combine(DataDir, "logs");
    public string StatePath => Path.Combine(DataDir, "state.txt");   // last processed event record id

    public AppPaths(IConfiguration config)
    {
        DataDir = config["Output:Directory"] is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RdpLogonLogger");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
    }
}
