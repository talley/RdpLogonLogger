using RdpSessionsViewer.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ExcelSessionReader>();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Sessions/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Sessions}/{action=Index}/{id?}");
app.Run();
