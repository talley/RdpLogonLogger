# RDP Session Viewer — ASP.NET Core MVC / C# / .NET 10

## Run on Windows
1. Install the .NET 10 SDK (the Desktop Runtime alone cannot build this application).
2. Open RdpSessionsViewer.csproj in Visual Studio with the ASP.NET and web development workload, or open PowerShell in this folder.
3. Check appsettings.json. Output:Directory uses the same folder as your logger: C:\ProgramData\RdpLogonLogger.
4. Set Excel:FileName to the actual file name on your computer. Default: rdp_sessions.xlsx. If your file really is called rdp_sessions(2).xlsx, enter that name instead. Excel:Worksheet is RdpSessions.
5. Run `dotnet restore`, then `dotnet run`. Open http://localhost:5097 or press F5 in Visual Studio.

The viewer reads all 13 columns from your workbook, including SessionLoggedOff. Search checks all columns, column headings sort, and pages offer 25/50/100 rows. Initial sorting is LoginDate descending, then LoginTime descending so the newest session within each day comes first. Sorting uses displayed text; ISO date strings sort chronologically, while numbers such as SessionId sort lexically. Blank fields remain blank and display as a dash. Razor HTML-encodes workbook content.

## How it works
Program.cs registers MVC and ExcelSessionReader. SessionsController reads the workbook and applies search, sorting and paging. The Razor view creates the HTML table. ClosedXML reads the workbook without requiring Microsoft Excel. The workbook is opened read-only with file sharing and copied to memory; it is never saved or modified. Each request reloads the data. If the logger holds an exclusive lock or is writing, retry Refresh after saving completes. File sharing cannot guarantee a consistent snapshot while another process rewrites the workbook; ideally the logger should save to a temporary file and atomically replace the completed workbook.

## IIS / another server
Publish with `dotnet publish -c Release -o ./publish`. Install the .NET 10 Hosting Bundle on IIS. Give the application pool identity (for example IIS AppPool\RdpSessionsViewer) Read/List permissions on the folder and Read permission on the workbook. Add the actual hostname to AllowedHosts. The path is resolved on the web server, not on the visitor's PC. If the logger runs on another computer, use a UNC path and an identity with both share and NTFS read permissions. Mapped drives are unsuitable for IIS.

The project defaults to localhost and has no authentication. Before making these user/IP logs available over a network, configure access control (for example IIS Windows Authentication with Anonymous disabled) and HTTPS. Do not place the Excel file in wwwroot.

## Verification
The supplied workbook was inspected: worksheet RdpSessions, 13 headers, 15 data rows. The source and configuration were reviewed, but compilation and runtime verification could not be performed in the delivery environment because the .NET SDK was not installed. Run `dotnet build` on Windows before deployment.

© 2026 Talley Ouro


## Printing and exports
- Print / Save PDF opens a separate report containing all matching rows, with repeating column headings and no scrolling container. Click its Print button and select a printer or Save as PDF. The stylesheet requests A3 landscape; adjust paper size/scale in the browser print dialog for your printer.
- Export Excel downloads a new .xlsx file with column headings, filters, and a frozen heading row. Exported values are text to preserve the source display, including leading zeros.
- Export CSV downloads UTF-8 CSV with a BOM for Excel. Quotes and commas are escaped; potential formula prefixes are neutralized with an apostrophe.
- All three actions preserve the applied search and sort and include all matching rows, across all pages. They read the latest saved workbook at request time. Clear the search to export or print everything.
- Default order is LoginDate DESC, LoginTime DESC. Column-heading sorting remains available; clicking Clear restores the default order.
- Upgrade: replace the project source with this version, preserving your local appsettings.json values, then rebuild. No extra NuGet packages are required beyond the original ClosedXML reference.
- Manual verification on Windows: build; verify newest dates and times first; search a user; compare exported/printed record count to matching count; test CSV and Excel in Excel; check print preview in landscape; test an unmatched search and an unavailable workbook.
