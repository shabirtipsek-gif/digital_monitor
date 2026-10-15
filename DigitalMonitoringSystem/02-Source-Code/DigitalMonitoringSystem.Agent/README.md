# A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT Monitoring Agent

This Windows agent sends CPU, memory and fixed-disk usage to the A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT API.

## Setup

1. Start the API.
2. Open the administrator dashboard.
3. Use **Enroll a device** and enter this computer's hostname and IP address.
4. Copy the returned `id` and `enrollmentToken`.
5. Put them in `appsettings.json` as `DeviceId` and `DeviceToken`.
6. Set `ApiBaseUrl` to the API address. On another computer on the same LAN, use the server's LAN IP, for example `http://192.168.1.20:5103`.
7. Run the agent.

The agent sends a heartbeat every 30 seconds. If it stops sending, the API marks the managed device Offline after 15 minutes and the default offline alert rule can raise an incident.

For automatic startup, create a Windows Task Scheduler task that starts the published agent at user logon or system startup.
