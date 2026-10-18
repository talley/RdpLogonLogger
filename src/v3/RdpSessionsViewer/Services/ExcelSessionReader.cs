using ClosedXML.Excel;
using System.Globalization;

namespace RdpSessionsViewer.Services;

public sealed class ExcelSessionReader(IConfiguration configuration)
{
    // Read each request so Refresh shows the logger's latest saved workbook.
    public (string[] Headers, List<string[]> Rows) Read()
    {
        var directory = configuration["Output:Directory"];
        var filename = configuration["Excel:FileName"];
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filename))
            throw new InvalidOperationException("Set Output:Directory and Excel:FileName in appsettings.json.");
        var path = Path.Combine(directory, filename);
        // Share with the logger and copy to memory to release the file handle quickly.
        using var snapshot = new MemoryStream();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            file.CopyTo(snapshot);
        snapshot.Position = 0;
        using var workbook = new XLWorkbook(snapshot);
        var sheetName = configuration["Excel:Worksheet"] ?? "RdpSessions";
        var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Worksheet '{sheetName}' was not found.");
        var range = sheet.RangeUsed();
        if (range is null) return ([], []);
        var firstRow = range.FirstRow().RowNumber();
        var firstColumn = range.FirstColumn().ColumnNumber();
        var count = range.ColumnCount();
        var headers = Enumerable.Range(firstColumn, count)
            .Select(c => sheet.Cell(firstRow, c).GetString()).ToArray();
        var rows = new List<string[]>();
        for (var r = firstRow + 1; r <= range.LastRow().RowNumber(); r++)
        {
            var values = Enumerable.Range(firstColumn, count)
                .Select(c => sheet.Cell(r, c).GetFormattedString(CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(values);
        }
        return (headers, rows);
    }
}
