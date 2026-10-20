#Requires -RunAsAdministrator
# Run from the project folder in an elevated PowerShell:  .\install.ps1
# (Recovery/restart options are set by the service itself on startup - see ServiceRecovery.cs)
$svc = "RdpLogonLogger"
$dir = "C:\Services\RdpLogonLogger"

dotnet publish "$PSScriptRoot\RdpLogonLogger.csproj" -c Release -r win-x64 --self-contained false -o $dir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

if (Get-Service $svc -ErrorAction SilentlyContinue) {
    sc.exe stop $svc | Out-Null
    Start-Sleep -Seconds 3
    sc.exe delete $svc | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $svc binPath= "`"$dir\RdpLogonLogger.exe`"" start= auto DisplayName= "RDP Logon Logger"
sc.exe description $svc "Records RDP connect, disconnect and logoff times to an Excel file."
sc.exe start $svc
