using Microsoft.AspNetCore.Mvc;
using RdpSessionsViewer.Models;
using RdpSessionsViewer.Services;

namespace RdpSessionsViewer.Controllers;

public sealed class SessionsController(ExcelSessionReader reader, ILogger<SessionsController> logger) : Controller
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index(string? search, int page = 1, int pageSize = 50, int sort = 6, string direction = "desc")
    {
        var model = new SessionsViewModel
        {
            Search = search?.Trim() ?? "", PageSize = new[] { 25, 50, 100 }.Contains(pageSize) ? pageSize : 50,
            Direction = direction == "asc" ? "asc" : "desc"
        };
        try
        {
            var (headers, rows) = reader.Read();
            model.Headers = headers;
            model.Total = rows.Count;
            model.Sort = headers.Length == 0 ? 0 : Math.Clamp(sort, 0, headers.Length - 1);
            IEnumerable<string[]> filtered = rows;
            if (model.Search.Length > 0)
                filtered = filtered.Where(r => r.Any(v => v.Contains(model.Search, StringComparison.OrdinalIgnoreCase)));
            var matches = filtered.ToList();
            model.Matched = matches.Count;
            model.Page = Math.Clamp(page, 1, model.Pages);
            if (headers.Length > 0)
            {
                // The logger uses ISO date strings, which sort in chronological order.
                filtered = model.Direction == "asc"
                    ? matches.OrderBy(r => r[model.Sort], StringComparer.OrdinalIgnoreCase)
                    : matches.OrderByDescending(r => r[model.Sort], StringComparer.OrdinalIgnoreCase);
                model.Rows = filtered.Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToArray();
            }
        }
        catch (FileNotFoundException) { model.Error = "Excel file not found. Check Output:Directory and Excel:FileName in appsettings.json."; }
        catch (DirectoryNotFoundException) { model.Error = "The configured Excel directory does not exist on this web server."; }
        catch (UnauthorizedAccessException) { model.Error = "The web application's Windows account needs Read permission on the Excel folder and file."; }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Excel file read failed");
            model.Error = "The Excel file is busy or temporarily unavailable. Wait for the logger to finish saving, then click Refresh.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to read RDP workbook");
            model.Error = "Unable to read the workbook. Check that it is a valid .xlsx file and Excel:Worksheet matches its sheet name. See application logs for details.";
        }
        return View(model);
    }

    public IActionResult Error() => Problem("An unexpected error occurred. See the application logs for details.");
}
