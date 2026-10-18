using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using System.Text;
using RdpSessionsViewer.Models;
using RdpSessionsViewer.Services;

namespace RdpSessionsViewer.Controllers;

public sealed class SessionsController(ExcelSessionReader reader, ILogger<SessionsController> logger) : Controller
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index(string? search, int page = 1, int pageSize = 50, int sort = 6, string direction = "desc")
    {
        return View(Load(search, page, pageSize, sort, direction));
    }

    private SessionsViewModel Load(string? search, int page, int pageSize, int sort, string direction, bool allRows = false)
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
                IOrderedEnumerable<string[]> ordered = model.Direction == "asc"
                    ? matches.OrderBy(r => r[model.Sort], StringComparer.OrdinalIgnoreCase)
                    : matches.OrderByDescending(r => r[model.Sort], StringComparer.OrdinalIgnoreCase);
                // ISO yyyy-MM-dd and HH:mm:ss values sort chronologically as text.
                var loginDate = Array.FindIndex(headers, h => h.Equals("LoginDate", StringComparison.OrdinalIgnoreCase));
                var loginTime = Array.FindIndex(headers, h => h.Equals("LoginTime", StringComparison.OrdinalIgnoreCase));
                if (model.Sort == loginDate && loginTime >= 0)
                    ordered = model.Direction == "asc"
                        ? ordered.ThenBy(r => r[loginTime], StringComparer.OrdinalIgnoreCase)
                        : ordered.ThenByDescending(r => r[loginTime], StringComparer.OrdinalIgnoreCase);
                model.Rows = allRows ? ordered.ToArray()
                    : ordered.Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToArray();
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
        return model;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Print(string? search, int sort = 6, string direction = "desc")
        => View(Load(search, 1, 50, sort, direction, allRows: true));

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Export(string format = "xlsx", string? search = null, int sort = 6, string direction = "desc")
    {
        if (format != "xlsx" && format != "csv") return BadRequest("Choose xlsx or csv.");
        var model = Load(search, 1, 50, sort, direction, allRows: true);
        if (model.Error is not null) return Problem(detail: model.Error, statusCode: 503);
        var name = $"rdp_sessions_{DateTime.Now:yyyyMMdd_HHmmss}";
        if (format == "csv")
        {
            // Quote every field and neutralize spreadsheet formula prefixes.
            static string Csv(string value)
            {
                var trimmed = value.TrimStart();
                if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || value[0] is '\t' or '\r' or '\n'))
                    value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", model.Headers.Select(Csv)));
            foreach (var row in model.Rows) csv.AppendLine(string.Join(",", row.Select(Csv)));
            var utf8 = new UTF8Encoding(true);
            return File(utf8.GetPreamble().Concat(utf8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", name + ".csv");
        }
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("RdpSessions");
        for (var c = 0; c < model.Headers.Count; c++) sheet.Cell(1, c + 1).Value = model.Headers[c];
        for (var r = 0; r < model.Rows.Count; r++)
            for (var c = 0; c < model.Headers.Count; c++)
                sheet.Cell(r + 2, c + 1).Value = model.Rows[r][c]; // Text values, never formulas.
        if (model.Headers.Count > 0)
        {
            sheet.Range(1, 1, 1, model.Headers.Count).Style.Font.Bold = true;
            sheet.Range(1, 1, Math.Max(1, model.Rows.Count + 1), model.Headers.Count).SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
            sheet.Columns(1, model.Headers.Count).AdjustToContents();
        }
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return File(output.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
    }

    public IActionResult Error() => Problem("An unexpected error occurred. See the application logs for details.");
}
