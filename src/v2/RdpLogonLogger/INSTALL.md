# Install / update (elevated PowerShell, on the server)

    .\install.ps1

It publishes the app to C:\Services\RdpLogonLogger, (re)creates the service and starts it.
On every start the service sets its own Recovery tab (services.msc > RdpLogonLogger > Properties > Recovery):
Restart the Service for first / second / subsequent failures, reset fail count after 0 days, restart after 1 minute.

# Where things are
- Excel:  C:\ProgramData\RdpLogonLogger\rdp_sessions.xlsx
- Log:    C:\ProgramData\RdpLogonLogger\logs\service-YYYYMMDD.log   (kept 30 days)
- State:  C:\ProgramData\RdpLogonLogger\state.txt  (last event processed; lets the service catch up after a restart)

# Uninstall
    sc.exe stop RdpLogonLogger
    sc.exe delete RdpLogonLogger

# If rows are missing, compare against Windows itself
Event Viewer > Applications and Services Logs > Microsoft > Windows > TerminalServices-LocalSessionManager > Operational
Events 21/25 = connect/reconnect, 24 = disconnect, 23 = logoff. Every one of those should show up as a
"RECEIVED" line in the service log.
