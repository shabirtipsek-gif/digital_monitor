A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY
DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT
==========================================================================

NOTHING TO INSTALL. Everything needed (including Node.js) is in this folder.

1. START THE SERVER (on the computer that will act as the server)
   - Extract the whole zip. Double-click  START-SYSTEM.bat
   - The dashboard opens in your browser at http://localhost:5103
   - Sign in:  admin / Admin@123   (change it in config.json, then restart)
   - Keep the black window open. Closing it stops monitoring.

2. LET OTHER COMPUTERS REACH THE SERVER (once)
   - Right-click ALLOW-NETWORK-ACCESS.bat > Run as administrator.
   - The server window shows the network address, e.g. http://192.168.1.20:5103

3. ADD (ENROLL) A NEW COMPUTER
   - Dashboard > "Enroll a device" > type hostname + IP address > Enroll device.
   - Click "Download appsettings.json".
   - Copy the  agent  folder (use DMS-Agent-for-Computers.zip for other PCs)
     to that computer and replace its appsettings.json with the downloaded file.
   - Double-click RUN-AGENT.bat. Within seconds the computer shows ONLINE with
     live CPU, memory and disk figures.
   - Optional: INSTALL-AUTOSTART.bat (as administrator) makes the agent start
     automatically whenever the computer starts.

WHAT YOU GET
   - Administrator login, device enrollment, removal, token re-issue
   - Live dashboard (updates instantly), CPU/memory/disk history per device
   - 200 demo-only inventory rows for dashboard demonstration; add real devices through enrollment
   - Pending verification review list with 200 imported entries (not verified or enrolled computers)
   - Automatic alerts: high CPU (5 min), high memory (5 min), disk > 90%,
     device stopped reporting (15 min). Alerts clear by themselves on recovery.
   - Alert acknowledgement, policy assignment, mobile pairing codes, CSV export
   - Data is stored in data\mdm.db (back this file up).

NOTES
   - Works fully offline on a local network (no internet needed).
   - Demo inventory rows are explicitly labeled DEMO, use documentation-only IP addresses, and have no
     agent tokens or heartbeat data. They are not real or live devices. A row becomes Online only after
     its enrolled agent sends a heartbeat. Additional real devices can be enrolled beyond the 200 demos.
   - The pending review list's source explicitly identifies all 200 rows as fictional test records and
     uses placeholder serial numbers/models. Pending review does not verify existence or district ownership.
     Check an authorized asset register, purchase records, and the physical devices before recording ownership.
     These entries are separate from live monitoring and are not enrolled computers.
   - Complete the parent-folder file 200-DEVICE-LIVE-MONITORING-TRACKER.csv with verified device
     details and recorded approval before enrolling any computer. Never put device tokens or passwords in it.
   - Policy "enforce" records the assignment; it does not change Windows
     settings by itself.
   - Re-issuing a device token disables the old token for that computer.
