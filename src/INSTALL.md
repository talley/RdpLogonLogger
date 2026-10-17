# Build & install (run in elevated PowerShell on the server)

dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\RdpLogonLogger

sc.exe create RdpLogonLogger binPath= "C:\Services\RdpLogonLogger\RdpLogonLogger.exe" start= auto obj= LocalSystem
sc.exe start RdpLogonLogger

# Remove:
sc.exe stop RdpLogonLogger
sc.exe delete RdpLogonLogger

# Make sure logon auditing is on (usually default on Windows Server):
auditpol /get /subcategory:"Logon"
auditpol /set /subcategory:"Logon" /success:enable
