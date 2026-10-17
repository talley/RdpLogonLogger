using RdpLogonLogger;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "RdpLogonLogger");
builder.Services.AddSingleton<ExcelStore>();
builder.Services.AddHostedService<RdpLogonWorker>();
builder.Build().Run();
