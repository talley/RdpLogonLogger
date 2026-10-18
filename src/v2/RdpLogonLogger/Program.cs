using RdpLogonLogger;
using Serilog;

try
{
    var builder = Host.CreateApplicationBuilder(args);
    var paths = new AppPaths(builder.Configuration);

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(
            Path.Combine(paths.LogsDir, "service-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true,                                   // lets you open the log in Notepad while it runs
            flushToDiskInterval: TimeSpan.FromSeconds(1),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    // Anything that would otherwise kill the process silently ends up in the log file.
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception - process is terminating");
        Log.CloseAndFlush();
    };
    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    };

    Log.Information("==== RdpLogonLogger starting (machine {Machine}, .NET {Ver}) ====",
        Environment.MachineName, Environment.Version);

    builder.Logging.AddSerilog(Log.Logger, dispose: true);
    builder.Services.AddWindowsService(o => o.ServiceName = "RdpLogonLogger");
    builder.Services.AddSingleton(paths);
    builder.Services.AddSingleton<ExcelStore>();
    builder.Services.AddHostedService<RdpLogonWorker>();

    builder.Build().Run();

    Log.Information("==== RdpLogonLogger stopped normally ====");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;           // non-zero exit => Windows treats it as a failure and restarts the service
}
finally
{
    Log.CloseAndFlush();
}
