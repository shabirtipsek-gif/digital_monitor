# A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT

This project contains the API, web dashboard, Windows monitoring agent, optional desktop dashboard, SQLite database and automated tests.

## Architecture

- `DigitalMonitoringSystem.Api` — ASP.NET Core REST API, SignalR dashboard hub, SQLite persistence and alert engine.
- `DigitalMonitoringSystem.Agent` — Windows monitoring agent. Sends CPU, memory and disk telemetry every 30 seconds.
- `mdm-frontend` — React/Vite administrator dashboard.
- `DigitalMonitoringSystem.Desktop` — optional WPF administrator dashboard.
- `DigitalMonitoringSystem.Tests` — unit tests.
- `DigitalMonitoringSystem.Shared` — shared contracts/models.

## Pending asset verification

The dashboard's **Pending verification** view presents 200 imported entries from
`mdm-frontend/src/data/sample-inventory.csv` as unverified candidates, separate from live monitored devices. The source
explicitly describes the rows as fictional test records and uses placeholder serial numbers and models marked “To verify.”
The review state is not evidence that an item exists or is district-owned. Confirm entries against an authorized asset
register, purchase documentation, and physical inspection before recording any as Mukono District Local Government
property. These entries have no hostnames, IP addresses, or agent telemetry.

## Run the system on one Windows computer

### 1. Install prerequisites

Install:

- .NET 10 SDK for the API/tests
- .NET 8 SDK for the Windows agent/desktop client
- Node.js 20 or newer

### 2. Start the API

Open PowerShell in `DigitalMonitoringSystem.Api`:

```powershell
dotnet restore
dotnet run --launch-profile http
```

The API listens on port `5103` and is configured to bind to the local network (`0.0.0.0`).

Health check:

```text
http://localhost:5103/health
```

Development administrator credentials are currently:

- Username: `admin`
- Password: `Admin@123`

Change these before real deployment.

### 3. Start the web dashboard

Open another terminal in `mdm-frontend`:

```powershell
npm install
npm run dev
```

Open the Vite address shown in the terminal, normally:

```text
http://localhost:5173
```

### 4. Enroll a Windows computer

1. Sign in to the dashboard.
2. Open **Enroll a device**.
3. Enter the computer hostname and IP address.
4. Click **Enroll device**.
5. Save the returned **Device ID** and **Enrollment Token**.

### 5. Configure the monitoring agent

Build it:

```powershell
cd ..\DigitalMonitoringSystem.Agent
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

Edit `publish\appsettings.json`:

```json
{
  "ApiBaseUrl": "http://SERVER-IP:5103",
  "DeviceId": "DEV-...",
  "DeviceToken": "...",
  "IntervalSeconds": 30
}
```

Run:

```powershell
.\publish\DigitalMonitoringSystem.Agent.exe
```

The computer will then send CPU, memory and disk metrics automatically.

## LAN/offline deployment

The application does not require cloud access for normal operation. Put the API server and monitored computers on the same local network.

For example, if the API computer has address `192.168.1.20`, configure an agent as:

```json
{
  "ApiBaseUrl": "http://192.168.1.20:5103",
  "DeviceId": "DEV-...",
  "DeviceToken": "...",
  "IntervalSeconds": 30
}
```

Allow TCP port `5103` through the Windows Firewall on the API computer.

The web frontend can also be configured with:

```text
VITE_API_BASE_URL=http://192.168.1.20:5103
```

If the frontend is served from another computer, add its exact origin to `Cors:AllowedOrigins` or the corresponding environment variable.

## Monitoring behaviour

- Heartbeat interval: 30 seconds by default.
- Device is considered offline after 15 minutes without a heartbeat.
- CPU, memory and disk samples are retained for 7 days by default.
- Background alert evaluation runs every 30 seconds.
- CPU, memory and disk rules support sustained-duration checks.
- Offline rules can raise an incident when a managed computer stops reporting.
- SignalR pushes dashboard changes to connected administrator clients.

## Device-token security

A device token can only submit telemetry for the device to which it was issued. The heartbeat API rejects attempts to use one device token against another device ID.

## Mobile pairing

The dashboard can create a short-lived mobile pairing code. The code is persisted in SQLite and expires after 10 minutes. A mobile client can exchange the code through:

```text
POST /api/mobile/pair/complete
```

with `pairingCode`, `ipAddress` and `location` and receive a device ID plus device token.

## Policy management

Policies can be assigned to managed devices and their assignment is recorded in SQLite. The API reports the assignment state and the administrator who applied it.

The current project models policy definitions and assignments; it does not pretend that a generic policy description automatically changes Windows Group Policy. Actual OS enforcement should be implemented per policy type (for example Windows Defender, firewall, update schedule or software deployment) before calling a policy fully enforced at the operating-system level.

## Tests

From the solution root:

```powershell
dotnet test DigitalMonitoringSystem.slnx
```

## Important production changes

Before deploying beyond a development workstation:

- Change `Admin:Password` and `Jwt:Key` using environment variables or user secrets.
- Restrict `Cors:AllowedOrigins` to trusted frontend origins.
- Prefer HTTPS with a trusted internal certificate if credentials will cross an untrusted network.
- Protect the SQLite database file and backups.
- Run the monitoring agent as a Windows service or scheduled startup task.
- A new API database starts with no devices, alerts, or policies. Enroll real authorized devices; the API does not insert demonstration computers into the live fleet.
